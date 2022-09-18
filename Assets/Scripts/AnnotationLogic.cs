using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class AnnotationLogic : MonoBehaviour
{
    /// <summary>
    /// Minimal lenght of contour z
    /// </summary>
    private const int CONTOUR_THRESH = 1000;

    private const int CONTOUR_OFFSET = 10;

    private const double CAMERA_MOVEMENT_LOWER_THRESHOLD = 0.01;

    private const double CAMERA_MOVEMENT_UPPER_THRESHOLD = 0.1;

    private const int LINE_STEP_IN_PX = 20;

    private const int TEXT_OFFSET = 10;

    private delegate double SplitFunction(Point2D p1, Point2D p2, Point2D p);

    private RenderTexture RenderTexture;
    private Texture2D processingTexture;
    private bool IsTransformComputing = false;
    private Resolution cameraResolution;

    private static int TEXT_HEIGHT;

    // ModelID -> ID
    private TwoWayDictionary<int, float> ModelIDMapping { get; set; }

    // ModelID -> Model
    private Dictionary<int, GameObject> ModelObjects { get; set; }

    // List of empty gameobject containing line renderers. One for each model
    private List<GameObject> LineRendererObjects { get; set; }
    private List<GameObject> TextLabelObjects { get; set; }
    private List<GameObject> CurrentlyVisibleLineRendererObjects { get; set; }

    private Vector3 LastRenderCameraPosition { get; set; }

    private MovementMode LastCameraMove { get; set; }

    //TODO: odstanit
    private List<Point2D> a { get; set; }
    private bool saveRender { get; set; }

    // Start is called before the first frame update
    void Start()
    {
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        // create artificial model id mapping
        ModelIDMapping = new TwoWayDictionary<int, float>();
        ModelObjects = new Dictionary<int, GameObject>();
        LineRendererObjects = new List<GameObject>();
        TextLabelObjects = new List<GameObject>();
        CurrentlyVisibleLineRendererObjects = new List<GameObject>();
        LastCameraMove = MovementMode.Normal;

        Material lineMaterial = Resources.Load<Material>("LineMaterial");

        int id = 1;
        saveRender = false;
        foreach (Transform child in GameObject.FindWithTag("Model").transform)
        {
            ModelIDMapping.Add(child.GetInstanceID(), (id++) / 255.0f);
            ModelObjects.Add(child.GetInstanceID(), child.gameObject);

            GameObject go = new GameObject();
            go.AddComponent<LineRenderer>();
            go.GetComponent<LineRenderer>().enabled = false;
            go.GetComponent<LineRenderer>().material = lineMaterial;
            LineRendererObjects.Add(go);


            GameObject g = new GameObject();
            g.AddComponent<TextMeshPro>();
            var tmp = g.GetComponent<TextMeshPro>();
            tmp.enabled = false;
            tmp.enableWordWrapping = false;
            tmp.rectTransform.sizeDelta = new Vector2(0, 0);
            tmp.color = Color.white;
            tmp.outlineColor = Color.black;
            tmp.outlineWidth = 1f;
            tmp.rectTransform.localScale = new Vector3(-0.001f, 0.001f, 0.001f);
            tmp.SetText(child.gameObject.name);
            TextLabelObjects.Add(g);
            TEXT_HEIGHT = (int)tmp.rectTransform.rect.height;
        }
        
        cameraResolution = Screen.currentResolution;
        RenderTexture = new RenderTexture(cameraResolution.width, cameraResolution.height, 16);
        RenderTexture.Create();
        RenderTexture.name = "LabelTexture";

        Camera c = GameObject.Find("LabelRenderCamera").GetComponent<Camera>();
        c.targetTexture = RenderTexture;
        c.enabled = false;
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.touchCount == 1 && Input.touches[0].phase == TouchPhase.Began)
            saveRender = true;

        Camera c = GameObject.Find("LabelRenderCamera").GetComponent<Camera>();

        // set labelling shader
        Shader shader = Shader.Find("Unlit/LabellingShader");
        foreach (Transform child in GameObject.FindWithTag("Model").transform)
        {
            GameObject obj = child.gameObject;
            MeshRenderer r = obj.GetComponent<MeshRenderer>();
            r.material.shader = shader;
            r.receiveShadows = false;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            r.material.SetFloat("_ModelID", ModelIDMapping.GetValue(child.GetInstanceID()));
        }

        // position second camera same as the main one
        c.transform.SetPositionAndRotation(Camera.current.transform.position, Camera.current.transform.rotation);
        if (Screen.currentResolution.width != cameraResolution.width || Screen.currentResolution.height != cameraResolution.height)
        {
            // screen has been tilted
            cameraResolution = Screen.currentResolution;
            RenderTexture = new RenderTexture(cameraResolution.width / 2, cameraResolution.height / 2, 16);
            RenderTexture.Create();
            RenderTexture.name = "LabelTexture";
            c.targetTexture = RenderTexture;
        }
        c.Render();

        // if none of the previous frames is being processed, proceed
        if (!IsTransformComputing)
        {
            IsTransformComputing = true;

            // Destroy old texture, create new one
            Destroy(processingTexture);
            processingTexture = new Texture2D(RenderTexture.width, RenderTexture.height, TextureFormat.ARGB32, false);

            // copy render texture to memory
            RenderTexture.active = RenderTexture;
            Rect textureCopyRegion = new Rect(0, 0, RenderTexture.width, RenderTexture.height);
            processingTexture.ReadPixels(textureCopyRegion, 0, 0);
            processingTexture.Apply();

            // invoke label computation
            ComputeLabels(processingTexture);

            RenderTexture.active = null;
        }

        // return original shader
        foreach (Transform child in GameObject.FindWithTag("Model").transform)
        {
            GameObject obj = child.gameObject;
            obj.GetComponent<MeshRenderer>().material.shader = Shader.Find("Standard");
        }
    }

    int i = 0;
    private void ComputeLabels(Texture2D input)
    {
        Task t = new Task(() =>
        {
            Camera c = GameObject.Find("LabelRenderCamera").GetComponent<Camera>();
            float minZCoordinate = float.MaxValue;

            // array to store distances
            var tex = new short[input.width * input.height];
            // array to store points: X,Y are coords to the closest edge point, in Distance is the distance
            var contourDistances = new Point2D[input.width, input.height];

            // pixel value => Tuple (max, coord)
            Dictionary<float, Tuple<int, Point2D>> MaxCoordinates = new Dictionary<float, Tuple<int, Point2D>>();

            //forward
            for (int x = 0; x < input.width; x++)
            {
                for (int y = 0; y < input.height; y++)
                {
                    Color pixelColor = input.GetPixel(x, y);
                    float pixel = pixelColor.r;

                    if (pixelColor.g != 0 && pixelColor.g < minZCoordinate)
                        minZCoordinate = pixelColor.g;

                    if (pixel == 0)
                        SetArrayValue<short>(input.width, tex, x, y, 0);
                    else
                    {
                        float topPixel = y == 0 ? 0 : input.GetPixel(x, y - 1).r;
                        float leftPixel = x == 0 ? 0 : input.GetPixel(x - 1, y).r;

                        if (pixel == topPixel && pixel == leftPixel)
                        {
                            short topDistance = y == 0 ? (short)0 : GetArrayValue(input.width, tex, x, y - 1);
                            short leftDistance = y == 0 ? (short)0 : GetArrayValue(input.width, tex, x - 1, y);
                            short outputPixel = (short)(Math.Min(topDistance, leftDistance) + 1);

                            SetArrayValue(input.width, tex, x, y, outputPixel);
                        }
                        else
                        {
                            SetArrayValue<short>(input.width, tex, x, y, 1);
                        }
                    }
                }
            }

            // backward
            for (int x = input.width - 1; x >= 0; x--)
            {
                for (int y = input.height - 1; y >= 0; y--)
                {
                    float pixel = input.GetPixel(x, y).r;

                    if (pixel != 0)
                    {
                        float bottomPixel = y == input.height - 1 ? 0 : input.GetPixel(x, y + 1).r;
                        float rightPixel = x == input.width - 1 ? 0 : input.GetPixel(x + 1, y).r;

                        short outputPixel = 1;
                        if (pixel == bottomPixel && pixel == rightPixel)
                        {
                            short bottomDistance = y == input.width - 1 ? (short)0 : GetArrayValue(input.width, tex, x, y + 1);
                            short rightDistance = y == input.height - 1 ? (short)0 : GetArrayValue(input.width, tex, x + 1, y);
                            outputPixel = (short)Math.Min((Math.Min(bottomDistance, rightDistance) + 1), GetArrayValue(input.width, tex, x, y));
                        }

                        if (MaxCoordinates.TryGetValue(pixel, out Tuple<int, Point2D> value))
                        {
                            int oldDistance = value.Item1;
                            if (outputPixel > oldDistance)
                                MaxCoordinates[pixel] = new Tuple<int, Point2D>(outputPixel, new Point2D(x, y));
                        }
                        else
                        {
                            MaxCoordinates.Add(pixel, new Tuple<int, Point2D>(1, new Point2D(x, y)));
                        }

                        SetArrayValue(input.width, tex, x, y, outputPixel);
                    }
                }
            }

            List<Point2D> anchors = MaxCoordinates.Select(x => x.Value.Item2).ToList();
            a = anchors;
            if (anchors.Count == 0)
            {
                Debug.Log($"v renderu nejsou zadne kotvy");
                // no object within the scene
                IsTransformComputing = false;
                LastCameraMove = MovementMode.Far;
                return;
            }

            try
            {
                // compute distance from current camera position to one in which the last computation happened
                // if camera movement distance is under threshold value, do not change labels
                Transform currentPosition = GameObject.FindGameObjectWithTag("MainCamera").transform;
                double distance = 0;
                if (LastRenderCameraPosition != null)
                {
                    distance = Math.Sqrt(
                        Math.Pow(currentPosition.position.x - LastRenderCameraPosition.x, 2)
                        + Math.Pow(currentPosition.position.y - LastRenderCameraPosition.y, 2)
                        + Math.Pow(currentPosition.position.z - LastRenderCameraPosition.z, 2));
                }

                // camera is somewhat still, dont use new values, and did not come from further distance
                if (distance < CAMERA_MOVEMENT_LOWER_THRESHOLD && LastRenderCameraPosition != null && LastCameraMove == MovementMode.Close)
                {
                    Debug.Log("camera still - output");
                    LastCameraMove = MovementMode.Close;
                    IsTransformComputing = false;
                    return;
                }
                else if (distance < CAMERA_MOVEMENT_LOWER_THRESHOLD && LastRenderCameraPosition != null && LastCameraMove == MovementMode.Normal)
                {
                    LastCameraMove = MovementMode.Close;
                }
                else
                {
                    LastCameraMove = MovementMode.Normal;
                    LastRenderCameraPosition = new Vector3(currentPosition.position.x, currentPosition.position.y, currentPosition.position.z);
                }

                var points = GetControurPoints(input);
                var hull = GrahamScan.Compute(points);
                List<Vector3> cont = hull.Select((x, i) => new Vector3(x.x, x.y, i)).ToList();
                var result = SplitContour(anchors, cont);
                // hide old visible lines
                foreach (var visibleGO in CurrentlyVisibleLineRendererObjects)
                {
                    var r = visibleGO.GetComponent<LineRenderer>();
                    r.enabled = false;
                }
                CurrentlyVisibleLineRendererObjects.Clear();

                foreach (var visibleGO in TextLabelObjects)
                {
                    var r = visibleGO.GetComponent<TextMeshPro>();
                    r.enabled = false;
                }

                // camera moved to far from the last render location -> hide all values and wait for more still movement
                if (distance > CAMERA_MOVEMENT_UPPER_THRESHOLD)
                {
                    Debug.Log("camera moving too fast - output");
                    LastCameraMove = MovementMode.Far;
                    IsTransformComputing = false;
                    return;
                }

                // pair = anchor -> ending line point
                foreach (var tuple in result)
                {
                    float artificialID = input.GetPixel(tuple.Item1.X, tuple.Item1.Y).r;

                    var v1 = c.ScreenToWorldPoint(new Vector3(tuple.Item1.X, tuple.Item1.Y, input.GetPixel(tuple.Item1.X, tuple.Item1.Y).g));
                    var v2 = c.ScreenToWorldPoint(new Vector3(tuple.Item2.X, tuple.Item2.Y, minZCoordinate));

                    GameObject lineGO = LineRendererObjects[(int)(artificialID * 255.0f) - 1];
                    var renderer = lineGO.GetComponent<LineRenderer>();
                    renderer.SetPositions(new Vector3[2] { v1, v2 });
                    renderer.enabled = true;
                    
                    GameObject labelGO = TextLabelObjects[(int)(artificialID * 255.0f) - 1];
                    var tmPro = labelGO.GetComponent<TextMeshPro>();
                    tmPro.enabled = true;
                    Point2D textPos = tuple.Item3.AlignText(tmPro.GetRenderedValues(true), tuple.Item2);
                    tmPro.rectTransform.position = c.ScreenToWorldPoint(new Vector3(textPos.X, textPos.Y, minZCoordinate));
                    labelGO.transform.LookAt(c.transform.position, transform.up);

                    CurrentlyVisibleLineRendererObjects.Add(lineGO);
                }
            }
            catch (Exception ex)
            {
                Debug.Log($"uplne venku {ex.Message}");
            }
            finally
            {
                if (saveRender)
                {
                    //TESTING
                    foreach (var nevim in a)
                    {
                        input.SetPixel(nevim.X, nevim.Y, Color.red);
                    }

                    var index = i++;

                    byte[] texx = input.EncodeToPNG();
                    File.WriteAllBytes(Application.persistentDataPath + $"/hello{index}.png", texx);
                    Debug.Log("RENDER SAVED!");

                    ScreenCapture.CaptureScreenshot($"/screenshot{index}.png");
                    Debug.Log("SCREENSHOT SAVED!");

                    saveRender = false;
                }
            }

            Debug.Log("new output");
            IsTransformComputing = false;
        })
        {

        };
        t.Start();
    }

    private static List<Tuple<Point2D, Point2D, TextAlignment>> SplitContour(List<Point2D> anchors, List<Vector3> contour)
    {
        Node root = new Node();
        root.Level = 0;

        ComputeDivisionLine(anchors, out double slope, out double constant);
        Point2D centerOfMass = ComputerCenterOfMass(anchors);
        Point2D p1 = new Point2D(0, (int)constant);
        Point2D p2 = new Point2D((int)(-constant / slope), 0);

        Point2D Q = PointLineIntersection(centerOfMass, p1, p2);
        Point2D u = new Point2D(p2.Y - p1.Y, -p2.X + p1.X);

        //move center of mass in case of it being on the line or close to it
        double tmp = Math.Sqrt(Math.Pow(u.X, 2) + Math.Pow(u.Y, 2));
        u.X /= (int)tmp;
        u.Y /= (int)tmp;
        centerOfMass = new Point2D(centerOfMass.X + 100 * u.X, centerOfMass.Y + 100 * u.Y);

        root.Q = Q;
        root.P = centerOfMass;

        contour = EnglargeHull(contour, CONTOUR_OFFSET);
        DividePointsList(anchors, contour, root, contour.Count);
        ComputeIntersections(root, contour);
        return AssignPoints(root);
    }

    /// <summary>
    /// Move all contour points from their center of mass
    /// </summary>
    /// <param name="contour">Contour points</param>
    /// <param name="offset">Offset of which to move the points</param>
    /// <returns></returns>
    private static List<Vector3> EnglargeHull(List<Vector3> contour, int offset)
    {
        var center = ComputerCenterOfMass(contour.Select(x => x.ToPoint()).ToList());
        List<Vector3> results = new List<Vector3>();

        foreach (var point in contour)
        {
            Vector2 dir = new Vector2(point.x - center.X, point.y - center.Y);
            dir *= offset / dir.magnitude;
            results.Add(new Vector3(point.x + dir.x, point.y + dir.y, point.z));
        }

        return results;
    }

    private static Tuple<Vector3, Vector3> ComputeIntersections(Node node, List<Vector3> allHullPoints)
    {
        // list vraci bud prvni a posledni prvek z mnoziny kontury nebo null, pokud v konture nejsou zadne body
        if (node.IsLeaf)
        {
            if (node.HullPoints.Count == 0)
                return null;
            return new Tuple<Vector3, Vector3>(node.HullPoints.First(), node.HullPoints.Last());
        }

        var fromLeft = ComputeIntersections(node.Left, allHullPoints);
        var fromRight = ComputeIntersections(node.Right, allHullPoints);

        Vector3 left, right;
        if (fromRight == null)
        {
            // prava cast v sobe nema zadne body obrysu
            // leva cast tedy body nutne ma -> zjistim si druhy bod a vezmu si nasledujici bod z mnoziny vsech bodu
            left = fromLeft.Item2;
            right = allHullPoints.ElementAt(((int)(left.z + 1)) % allHullPoints.Count);
        }
        else if (fromLeft == null)
        {
            // obracene jako s pravou
            right = fromRight.Item1;
            left = allHullPoints.ElementAt(((int)(right.z - 1)) % allHullPoints.Count);
        }
        else
        {
            left = fromLeft.Item2;
            right = fromRight.Item1;
        }

        Point2D intersection = LineHalfLineIntersection(node, left, right, out double _);
        node.ContourIntersection = intersection;

        if (node.Level == 0)
        {
            Node n = new Node();
            n.P = node.Q;
            n.Q = node.P;
            node.SecondContourIntersection = LineHalfLineIntersection(n, fromRight.Item2, fromLeft.Item1, out double _);
        }

        Vector3 returnLeft, returnRight;
        if (fromLeft == null)
        {
            returnLeft = new Vector3(intersection.X, intersection.Y, -1); ;
            returnRight = fromRight.Item2;
        }
        else if (fromRight == null)
        {
            returnLeft = fromLeft.Item1;
            returnRight = new Vector3(intersection.X, intersection.Y, -1);
        }
        else
        {
            returnLeft = fromLeft.Item1;
            returnRight = fromRight.Item2;
        }

        return new Tuple<Vector3, Vector3>(returnLeft, returnRight);
    }

    private static List<Tuple<Point2D, Point2D, TextAlignment>> AssignPoints(Node node)
    {
        List<Tuple<Point2D, Point2D, TextAlignment>> res = new List<Tuple<Point2D, Point2D, TextAlignment>>();
        res.AddRange(ComputePointEdgeLines(node.Left.Left.ContourIntersection, node.Left.ContourIntersection, node.Left.Left.Right, node.Left.Q, node.Left.P));
        res.AddRange(ComputePointEdgeLines(node.Left.ContourIntersection, node.Left.Right.ContourIntersection, node.Left.Right.Left, node.Left.Q, node.Left.P));

        res.AddRange(ComputePointEdgeLines(node.Left.Right.ContourIntersection, node.ContourIntersection, node.Left.Right.Right, node.Q, node.P));
        res.AddRange(ComputePointEdgeLines(node.ContourIntersection, node.Right.Left.ContourIntersection, node.Right.Left.Left, node.Q, node.P));

        res.AddRange(ComputePointEdgeLines(node.Right.Right.ContourIntersection, node.SecondContourIntersection, node.Right.Right.Right, node.Q, node.SecondContourIntersection));
        res.AddRange(ComputePointEdgeLines(node.SecondContourIntersection, node.Left.Left.ContourIntersection, node.Left.Left.Left, node.Q, node.SecondContourIntersection));

        res.AddRange(ComputePointEdgeLines(node.Right.Left.ContourIntersection, node.Right.ContourIntersection, node.Right.Left.Right, node.Right.Q, node.Right.P));
        res.AddRange(ComputePointEdgeLines(node.Right.ContourIntersection, node.Right.Right.ContourIntersection, node.Right.Right.Left, node.Right.Q, node.Right.P));
        return res;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="intersection1">Starting points of the contour points</param>
    /// <param name="intersection2">Ending points of the contour points</param>
    /// <param name="pointsNode">Node containing anchor points and hull points</param>
    /// <param name="function">Ordering function</param>
    /// <param name="lineNode">Node containing points from which the anchor order is compute</param>
    private static List<Tuple<Point2D, Point2D, TextAlignment>> ComputePointEdgeLines(Point2D intersection1, Point2D intersection2, Node pointsNode, Point2D lineStart, Point2D lineEnd)
    {
        List<Point2D> allPoints = new List<Point2D>();
        List<Tuple<Point2D, Point2D>> res = new List<Tuple<Point2D, Point2D>>();

        allPoints.Add(intersection1);
        allPoints.AddRange(pointsNode.HullPoints.Select(x => x.ToPoint()));
        allPoints.Add(intersection2);

        List<Point2D> anchors = pointsNode.Anchors.OrderByDescending(x => DistanceToProjection(lineStart, lineEnd, x)).ToList();
        Vector2 directionalVector = new Vector2(lineEnd.X - lineStart.X, lineEnd.Y - lineStart.Y);
        double angle = Math.Atan2(directionalVector.y, directionalVector.x);
        double degrees = (180 / Math.PI) * angle;
        directionalVector /= directionalVector.magnitude;
        directionalVector *= 5;

        int textWidth = 50;

        List<Tuple<Point2D, Point2D, TextAlignment>> results = new List<Tuple<Point2D, Point2D, TextAlignment>>();
        for (int i = 0; i < anchors.Count; i++)
        {
            Point2D intersection = new Point2D(-1, -1);
            double minT = double.MaxValue;
            for (int j = 0; j < allPoints.Count - 1; j++)
            {
                Point2D inter = ProjectPointOnLineWithDirection(anchors[i], allPoints[j], allPoints[j + 1], directionalVector, out double t);

                if (inter != null && t < minT)
                {
                    intersection = inter;
                    minT = t;
                }
            }

            if (i != 0)
            {
                var previous = results[i - 1].Item2;
                int deltaY = degrees > 0 ? intersection.Y - previous.Y : previous.Y - intersection.Y;
                int deltaX = degrees > 0 ? intersection.X - previous.X : previous.X - intersection.X;

                if (deltaY < TEXT_HEIGHT + TEXT_OFFSET)
                {
                    Vector2 dir = new Vector2(intersection.X - anchors[i].X, intersection.Y - anchors[i].Y);
                    Vector2 dirVec = new Vector2(Math.Abs(directionalVector.x), Math.Abs(directionalVector.y));

                    // text ma dostatecne rovnomerny smer
                    if (Math.Abs(180 - degrees) <= 10 || Math.Abs(degrees) <= 10)
                    {
                        int missingX = textWidth - deltaX;

                        dirVec /= dirVec.x;
                        dirVec *= Math.Abs(dir.x) + missingX;
                    }
                    else
                    {
                        int missingX = TEXT_HEIGHT + TEXT_OFFSET - deltaY;

                        dirVec /= dirVec.y;
                        dirVec *= Math.Abs(dir.y) + missingX;
                    }

                    dirVec.x *= Math.Sign(dir.x);
                    dirVec.y *= Math.Sign(dir.y);
                    intersection = new Point2D((int)(anchors[i].X + dirVec.x), (int)(anchors[i].Y + dirVec.y));
                }
            }

            //pozor, degrees jsou -180 az 180, zaporne hodnoty jsou v 1. a 4. kvadrantu (otocene)
            TextAlignment alignment = TextAlignment.TopLeft;
            if (degrees >= -15 && degrees <= 15)
                alignment = TextAlignment.Right;
            else if (degrees >= 165 || degrees <= -165)
                alignment = TextAlignment.Left;
            else
            {
                float dot = EdgeFunction(lineStart, lineEnd, anchors[i]);
                if (degrees <= 0 && dot < 0)
                {
                    //horni polovina
                    alignment = TextAlignment.TopRight;
                }
                else if (degrees > 0)
                {
                    //dolni polovina
                    if (dot > 0)
                        alignment = TextAlignment.BottomRight;
                    else
                        alignment = TextAlignment.BottomLeft;
                }
            }

            results.Add(new Tuple<Point2D, Point2D, TextAlignment>(anchors[i], intersection, alignment));
        }

        return results;
    }

    private static float EdgeFunction(Point2D from, Point2D to, Point2D point)
    {
        Vector2 normalVector = new Vector2(-to.Y + from.Y, to.X - from.X);
        Vector2 vec = new Vector2(point.X - from.X, point.Y - from.Y);

        float dot = Vector2.Dot(normalVector, vec);
        return dot;
    }

    private static Point2D ProjectPointOnLineWithDirection(Point2D point, Point2D line1, Point2D line2, Vector2 direction, out double T)
    {
        Point2D normalVector = new Point2D(-line2.Y + line1.Y, line2.X - line1.X);

        double c = -normalVector.X * line1.X - normalVector.Y * line1.Y;
        double t = (-normalVector.X * point.X - normalVector.Y * point.Y - c) / (float)(normalVector.X * direction.x + normalVector.Y * direction.y);
        T = t;

        if (normalVector.X * direction.x + normalVector.Y * direction.y == 0 || t <= 0)
            return null;

        Point2D res = new Point2D((int)(point.X + t * direction.x), (int)(point.Y + t * direction.y));
        return res;
    }

    /// <summary>
    /// Compute distance from p1 to perpendicular pronection of Point point to the line from p1 to p2
    /// </summary>
    /// <param name="p1">Starting point of the line to project to</param>
    /// <param name="p2">Ending point of the line to project to</param>
    /// <param name="point">Point to project on the line</param>
    /// <returns>Distance between p1 and projection of point onto the line</returns>
    private static double DistanceToProjection(Point2D p1, Point2D p2, Point2D point)
    {
        Point2D projection = PointLineIntersection(point, p1, p2);
        Vector2 v = new Vector2(projection.X - point.X, projection.Y - point.Y);

        return v.magnitude;
    }

    /// <summary>
    /// Recursively traverse node tree and divide anchor points
    /// </summary>
    /// <param name="anchors">List of remaining anchors to be divided</param>
    /// <param name="currentNode">Curently processed node</param>
    private static void DividePointsList(List<Point2D> anchors, List<Vector3> hullPoints, Node currentNode, int numberOfHullPoints)
    {
        var subGroups = DividePointsByLine(anchors, currentNode.P, currentNode.Q);
        var subGroupsHull = DividePointsByLine(hullPoints, currentNode.P, currentNode.Q);

        Node left = new Node();
        left.Level = currentNode.Level + 1;
        left.P = ComputerCenterOfMass(subGroups[0]);
        left.Q = PointLineIntersection(left.P, currentNode.P, currentNode.Q);

        Node right = new Node();
        right.Level = currentNode.Level + 1;
        right.P = ComputerCenterOfMass(subGroups[1]);
        right.Q = PointLineIntersection(right.P, currentNode.P, currentNode.Q);

        currentNode.Left = left;
        currentNode.Right = right;

        if (currentNode.Level == 2)
        {
            left.Anchors = subGroups[0];
            left.HullPoints = new SortedSet<Vector3>(subGroupsHull[0], new VectorComparer(numberOfHullPoints, subGroupsHull[0].Count));
            right.Anchors = subGroups[1];
            right.HullPoints = new SortedSet<Vector3>(subGroupsHull[1], new VectorComparer(numberOfHullPoints, subGroupsHull[1].Count));
            return;
        }
        else
        {
            DividePointsList(subGroups[0], subGroupsHull[0], left, numberOfHullPoints);
            DividePointsList(subGroups[1], subGroupsHull[1], right, numberOfHullPoints);
        }
    }

    /// <summary>
    /// Find intersection between two lines: first one is represented by points p1 and p2; second one is a half line goint FROM node.Q TO node.P
    /// </summary>
    /// <param name="n">Current node containing P and Q</param>
    /// <param name="p1">First contour point</param>
    /// <param name="p2">Second contour point</param>
    /// <returns>Intersection of the two lines</returns>
    private static Point2D LineHalfLineIntersection(Node n, Vector3 p1, Vector3 p2, out double T, bool discardNegative = true)
    {
        Point2D dirVector = new Point2D(n.P.X - n.Q.X, n.P.Y - n.Q.Y);
        Point2D normalVector = new Point2D((int)(-p2.y + p1.y), (int)(p2.x - p1.x));

        int a = normalVector.X;
        int b = normalVector.Y;
        double c = -a * p1.x - b * p1.y;

        double t = (-c - a * n.Q.X - b * n.Q.Y) / (a * dirVector.X + b * dirVector.Y);
        T = t;

        if (t <= 0 && discardNegative)
            return null;

        Point2D intersection = new Point2D((int)(n.Q.X + t * dirVector.X), (int)(n.Q.Y + t * dirVector.Y));
        return intersection;
    }

    /// <summary>
    /// Compute point that is an average of X and Y components of list of points
    /// </summary>
    /// <param name="points">List of input points</param>
    /// <returns>Center of mass</returns>
    private static Point2D ComputerCenterOfMass(List<Point2D> points)
    {
        double avgX = points.Average(x => x.X);
        double avgY = points.Average(x => x.Y);
        return new Point2D((int)avgX, (int)avgY);
    }

    /// <summary>
    /// Divide given list of points into halfs depending on the side on which they lie
    /// </summary>
    /// <param name="points">Points to divide</param>
    /// <param name="p">First division line point</param>
    /// <param name="q">Second division line point</param>
    /// <returns></returns>
    private static List<List<Point2D>> DividePointsByLine(List<Point2D> points, Point2D p, Point2D q)
    {
        List<Point2D> left = new List<Point2D>();
        List<Point2D> right = new List<Point2D>();

        Vector2 u = new Vector2(-q.Y + p.Y, q.X - p.X);

        foreach (var point in points)
        {
            Vector2 v = new Vector2(point.X - p.X, point.Y - p.Y);
            float dot = Vector2.Dot(u, v);

            if (dot < 0)
                right.Add(point);
            else
                left.Add(point);
        }

        List<List<Point2D>> res = new List<List<Point2D>>();
        res.Add(left);
        res.Add(right);
        return res;
    }

    private static List<List<Vector3>> DividePointsByLine(List<Vector3> points, Point2D p, Point2D q)
    {
        List<Vector3> left = new List<Vector3>();
        List<Vector3> right = new List<Vector3>();

        Vector2 u = new Vector2(-q.Y + p.Y, q.X - p.X);

        foreach (var point in points)
        {
            Vector2 v = new Vector2(point.x - p.X, point.y - p.Y);
            float dot = Vector2.Dot(u, v);

            if (dot < 0)
                right.Add(point);
            else
                left.Add(point);
        }

        List<List<Vector3>> res = new List<List<Vector3>>();
        res.Add(left);
        res.Add(right);
        return res;
    }

    /// <summary>
    /// Project given point to the line given by points p1 and p2
    /// </summary>
    /// <param name="point">Point to project</param>
    /// <param name="p1">First line point</param>
    /// <param name="p2">Second line point</param>
    /// <returns></returns>
    private static Point2D PointLineIntersection(Point2D point, Point2D p1, Point2D p2)
    {
        Point2D normalVector = new Point2D(-p2.Y + p1.Y, p2.X - p1.X);
        int a = normalVector.X;
        int b = normalVector.Y;

        double c = -normalVector.X * p1.X - normalVector.Y * p1.Y;
        double t = (-a * point.X - b * point.Y - c) / (Math.Pow(a, 2) + Math.Pow(b, 2));

        Point2D intersection = new Point2D((int)(point.X + t * a), (int)(point.Y + t * b));

        return intersection;
    }

    /// <summary>
    /// Compute intersecting line using the least squares method
    /// </summary>
    /// <param name="anchors"></param>
    /// <param name="slope">Slope parameter (k) of the output line</param>
    /// <param name="constant">Constant parameter (q) of the output line</param>
    private static void ComputeDivisionLine(List<Point2D> anchors, out double slope, out double constant)
    {
        try
        {
            List<Point2D> availableAnchors = new List<Point2D>(anchors);
            double k = 0;
            double q = 0;

            // compute slope using remaining anchors
            double avgX = availableAnchors.Average(x => x.X);
            double avgY = availableAnchors.Average(x => x.Y);

            double tmpX = 0;
            double varX = 0;

            foreach (var point in availableAnchors)
            {
                tmpX += (point.X - avgX) * (point.Y - avgY);
                varX += Math.Pow(point.X - avgX, 2);
            }

            k = tmpX / varX;
            q = avgY - k * avgX;

            slope = k;
            constant = q;
        }
        catch (Exception ex)
        {
            Debug.Log($"ComputeDivisionLine {ex.Message}");
            slope = 0;
            constant = 0;
        }
    }

    /// <summary>
    /// Find points on the contour of the input image
    /// </summary>
    /// <param name="input">Input image</param>
    /// <returns>List of contour points</returns>
    private static List<Vector3> GetControurPoints(Texture2D input)
    {
        for (int x = 0; x < input.width; x++)
        {
            for (int y = 0; y < input.height; y++)
            {
                if (input.GetPixel(x, y).r != 0)
                {
                    var points = CrawlContourPoints(input, x, y, out int length);

                    if (length > CONTOUR_THRESH)
                        return points;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Find contour from starting point
    /// </summary>
    /// <param name="input"></param>
    /// <param name="x">Start x coordinate</param>
    /// <param name="y">Start y coordinate</param>
    /// <param name="lenght">Output leght of the found conture - used later to check whether sufficiently big contour was found</param>
    /// <returns></returns>
    private static List<Vector3> CrawlContourPoints(Texture2D input, int x, int y, out int lenght)
    {
        List<Vector3> res = new List<Vector3>();

        int _x = x;
        int _y = y;

        // 0 = top, 1 = right, 2 = bottom, 3 = left
        int direction = 1;
        bool next = true;
        int l = 0;

        while (next)
        {
            l += 1;

            float current;
            if (_x >= 0 && _x < input.width && _y >= 0 && _y < input.height)
            {
                current = input.GetPixel(_x, _y).r;
                res.Add(new Vector3(_x, _y, 0));
            }
            else
                current = 0;

            if (current == 0)
                direction -= 1;
            else
                direction += 1;

            if (direction < 0)
                direction += 4;

            direction %= 4;

            switch (direction)
            {
                case 0:
                    _y -= 1;
                    break;
                case 1:
                    _x += 1;
                    break;
                case 2:
                    _y += 1;
                    break;
                case 3:
                    _x -= 1;
                    break;
            }

            next = !(_x == x && _y == y);
        }

        lenght = l;
        return res;
    }

    private static void SetArrayValue<T>(int width, T[] output, int x, int y, T value)
    {
        int position = y * width + x;
        output[position] = value;
    }

    private static T GetArrayValue<T>(int width, T[] output, int x, int y)
    {
        int position = y * width + x;
        return output[position];
    }
}
