#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;
#endif
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ProceduralCableSkin : MonoBehaviour
{
    [Header("Source")]
    [Tooltip("Ordered control points along the cable.")]
    public Transform[] points;

    public enum PathMode
    {
        Linear,
        CatmullRom
    }

    [Header("Path Smoothing")]
    public PathMode pathMode = PathMode.CatmullRom;

    [Min(1)]
    [Tooltip("Subdivision steps for each segment between neighboring points. Higher values make the path smoother.")]
    public int stepsPerSegment = 6;

    [Header("Tube Shape")]
    [Min(0.0001f)]
    public float radius = 0.08f;

    [Min(3)]
    [Tooltip("Number of radial sides for the tube. Values around 8 to 16 are usually enough.")]
    public int sides = 10;

    [Tooltip("UV tiling multiplier. X = along cable length, Y = around the tube.")]
    public Vector2 uvMultiply = Vector2.one;

    [Header("Rendering")]
    [Tooltip("Generates reversed triangles for back faces. Doubles triangle count.")]
    public bool doubleSided = false;

    [Tooltip("Flips triangle winding if the mesh appears inside out.")]
    public bool flipFaces = false;

    [Tooltip("Flips the V coordinate around the tube.")]
    public bool flipV = false;

    [Header("Update")]
    [Tooltip("Rebuilds automatically during Play Mode.")]
    public bool autoUpdateInPlay = true;

    [Tooltip("Rebuilds automatically in Edit Mode.")]
    public bool autoUpdateInEditor = true;

    [Tooltip("Edit Mode rebuild interval in seconds.")]
    public float editorSyncInterval = 0.15f;

    MeshFilter _meshFilter;
    Mesh _mesh;
    float _nextEditorSyncTime;

#if UNITY_EDITOR
    bool _undoHooked;
#endif

    const float Epsilon = 1e-8f;

    void OnEnable()
    {
        EnsureMesh();
#if UNITY_EDITOR
        HookUndo();
#endif
        Rebuild();
    }

    void OnDisable()
    {
#if UNITY_EDITOR
        UnhookUndo();
#endif
    }

    void Update()
    {
        if (Application.isPlaying)
            return;

        if (!autoUpdateInEditor)
            return;

        float time = Time.realtimeSinceStartup;
        if (time >= _nextEditorSyncTime)
        {
            _nextEditorSyncTime = time + Mathf.Max(0.02f, editorSyncInterval);
            Rebuild();
        }
    }

    void LateUpdate()
    {
        if (!Application.isPlaying)
            return;

        if (!autoUpdateInPlay)
            return;

        Rebuild();
    }

    void EnsureMesh()
    {
        if (!_meshFilter)
            _meshFilter = GetComponent<MeshFilter>();

        if (!_meshFilter)
            return;

        if (_mesh == null || !_mesh)
        {
            _mesh = new Mesh
            {
                name = "ProceduralCableSkin_Mesh"
            };
            _mesh.MarkDynamic();
        }

        if (_meshFilter.sharedMesh != _mesh)
            _meshFilter.sharedMesh = _mesh;
    }

#if UNITY_EDITOR
    void HookUndo()
    {
        if (_undoHooked)
            return;

        Undo.undoRedoPerformed += OnUndoRedo;
        _undoHooked = true;
    }

    void UnhookUndo()
    {
        if (!_undoHooked)
            return;

        Undo.undoRedoPerformed -= OnUndoRedo;
        _undoHooked = false;
    }

    void OnUndoRedo()
    {
        if (!this)
            return;

        EditorApplication.delayCall += () =>
        {
            if (!this)
                return;

            Rebuild();
        };
    }

    string GetNewPointName()
    {
        int index = (points != null ? points.Length : 0) + 1;
        return "Point_" + index;
    }

    static Texture2D GetPointIconTexture()
    {
        GUIContent content = EditorGUIUtility.IconContent("sv_label_0");
        if (content != null && content.image != null)
            return content.image as Texture2D;

        content = EditorGUIUtility.IconContent("sv_icon_dot0_pix16_gizmo");
        if (content != null && content.image != null)
            return content.image as Texture2D;

        return null;
    }

    static void ApplyPointIcon(GameObject go)
    {
        if (!go)
            return;

        Texture2D icon = GetPointIconTexture();
        if (icon != null)
            EditorGUIUtility.SetIconForObject(go, icon);
    }

    static void SelectObjectDelayed(Object obj)
    {
        EditorApplication.delayCall += () =>
        {
            if (!obj)
                return;

            Selection.activeObject = obj;
            EditorGUIUtility.PingObject(obj);
        };
    }

    static string SanitizeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        for (int i = 0; i < invalid.Length; i++)
            name = name.Replace(invalid[i], '_');

        return name;
    }

    static void EnsureFolderRecursive(string assetFolder)
    {
        if (string.IsNullOrEmpty(assetFolder) || assetFolder == "Assets")
            return;

        string[] parts = assetFolder.Split('/');
        string current = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);

            current = next;
        }
    }

    void FinalizePointAdd(GameObject go)
    {
        ApplyPointIcon(go);

        EditorUtility.SetDirty(this);
        EditorUtility.SetDirty(go);

        if (!Application.isPlaying && gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(gameObject.scene);

        Rebuild();
        SelectObjectDelayed(go);
    }

    public void AddPointFromLast()
    {
        if (!this)
            return;

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Add Cable Point");

        Vector3 spawnPosition = transform.position;

        if (points != null && points.Length > 0)
        {
            Transform lastPoint = points[points.Length - 1];
            if (lastPoint)
                spawnPosition = lastPoint.position;
        }

        GameObject go = new GameObject(GetNewPointName());
        Undo.RegisterCreatedObjectUndo(go, "Add Cable Point");

        Transform newPoint = go.transform;
        newPoint.SetParent(transform, true);
        newPoint.position = spawnPosition;

        Undo.RecordObject(this, "Add Cable Point");

        int oldLength = points != null ? points.Length : 0;
        Transform[] newPoints = new Transform[oldLength + 1];

        for (int i = 0; i < oldLength; i++)
            newPoints[i] = points[i];

        newPoints[oldLength] = newPoint;
        points = newPoints;

        FinalizePointAdd(go);
        Undo.CollapseUndoOperations(undoGroup);
    }

    public void AddPointAtStartFromFirst()
    {
        if (!this)
            return;

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Add Cable Point At Start");

        Vector3 spawnPosition = transform.position;

        if (points != null && points.Length > 0)
        {
            Transform firstPoint = points[0];
            if (firstPoint)
                spawnPosition = firstPoint.position;
        }

        GameObject go = new GameObject(GetNewPointName());
        Undo.RegisterCreatedObjectUndo(go, "Add Cable Point At Start");

        Transform newPoint = go.transform;
        newPoint.SetParent(transform, true);
        newPoint.position = spawnPosition;

        Undo.RecordObject(this, "Add Cable Point At Start");

        int oldLength = points != null ? points.Length : 0;
        Transform[] newPoints = new Transform[oldLength + 1];

        newPoints[0] = newPoint;
        for (int i = 0; i < oldLength; i++)
            newPoints[i + 1] = points[i];

        points = newPoints;

        FinalizePointAdd(go);
        Undo.CollapseUndoOperations(undoGroup);
    }

    bool TryCreateBakedMeshAssetAtPath(string meshAssetPath, out Mesh bakedMesh)
    {
        bakedMesh = null;

        EnsureMesh();
        Rebuild();

        if (_mesh == null || !_mesh || _mesh.vertexCount <= 0)
        {
            EditorUtility.DisplayDialog("Bake Mesh", "No valid mesh available to bake.", "OK");
            return false;
        }

        string finalPath = AssetDatabase.GenerateUniqueAssetPath(meshAssetPath);

        Mesh meshCopy = Instantiate(_mesh);
        meshCopy.name = Path.GetFileNameWithoutExtension(finalPath);

        AssetDatabase.CreateAsset(meshCopy, finalPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        bakedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(finalPath);
        return bakedMesh != null;
    }

    bool TryCreateBakedMeshAuto(out Mesh bakedMesh, out string bakedMeshPath)
    {
        bakedMesh = null;
        bakedMeshPath = null;

        string folder = "Assets/_BakedMeshes";
        EnsureFolderRecursive(folder);

        string assetName = SanitizeFileName(gameObject.name) + "_BakedMesh.asset";
        string fullPath = folder + "/" + assetName;

        if (!TryCreateBakedMeshAssetAtPath(fullPath, out bakedMesh))
            return false;

        bakedMeshPath = AssetDatabase.GetAssetPath(bakedMesh);
        return bakedMesh != null;
    }

    public void BakeMeshAsset()
    {
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Bake Cable Mesh In Scene");

        if (!TryCreateBakedMeshAuto(out Mesh bakedMesh, out string bakedMeshPath))
            return;

        MeshRenderer sourceRenderer = GetComponent<MeshRenderer>();
        if (!sourceRenderer)
        {
            EditorUtility.DisplayDialog("Bake Mesh", "The object does not have a MeshRenderer.", "OK");
            return;
        }

        string originalName = gameObject.name;
        string sourceName = originalName.EndsWith("_SRC") ? originalName : originalName + "_SRC";
        string bakedName = originalName.EndsWith("_SRC")
            ? originalName.Substring(0, Mathf.Max(0, originalName.Length - 4))
            : originalName;

        Transform parent = transform.parent;
        int siblingIndex = transform.GetSiblingIndex();

        GameObject bakedObject = new GameObject(bakedName);
        Undo.RegisterCreatedObjectUndo(bakedObject, "Bake Cable Mesh In Scene");

        Transform bakedTransform = bakedObject.transform;
        bakedTransform.SetParent(parent, false);
        bakedTransform.localPosition = transform.localPosition;
        bakedTransform.localRotation = transform.localRotation;
        bakedTransform.localScale = transform.localScale;
        bakedTransform.SetSiblingIndex(siblingIndex);

        bakedObject.layer = gameObject.layer;
        bakedObject.tag = gameObject.tag;
        GameObjectUtility.SetStaticEditorFlags(bakedObject, GameObjectUtility.GetStaticEditorFlags(gameObject));

        MeshFilter bakedMeshFilter = bakedObject.AddComponent<MeshFilter>();
        MeshRenderer bakedMeshRenderer = bakedObject.AddComponent<MeshRenderer>();

        EditorUtility.CopySerialized(sourceRenderer, bakedMeshRenderer);
        bakedMeshFilter.sharedMesh = bakedMesh;
        bakedMeshRenderer.sharedMaterials = sourceRenderer.sharedMaterials;

        Undo.RecordObject(gameObject, "Bake Cable Mesh In Scene");
        gameObject.name = sourceName;
        gameObject.SetActive(false);

        EditorUtility.SetDirty(this);
        EditorUtility.SetDirty(gameObject);
        EditorUtility.SetDirty(bakedObject);

        if (!Application.isPlaying && gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(gameObject.scene);

        SelectObjectDelayed(bakedObject);
        Undo.CollapseUndoOperations(undoGroup);

        Debug.Log("Cable baked to mesh: " + bakedMeshPath, bakedObject);
    }

    public void BakePrefabAsset()
    {
        string prefabPath = EditorUtility.SaveFilePanelInProject(
            "Save Baked Prefab",
            gameObject.name + "_Baked",
            "prefab",
            "Choose where to save the baked prefab."
        );

        if (string.IsNullOrEmpty(prefabPath))
            return;

        string folder = Path.GetDirectoryName(prefabPath);
        if (string.IsNullOrEmpty(folder))
            folder = "Assets";

        string baseName = Path.GetFileNameWithoutExtension(prefabPath);
        string meshPath = folder.Replace("\\", "/") + "/" + baseName + "_Mesh.asset";

        if (!TryCreateBakedMeshAssetAtPath(meshPath, out Mesh bakedMesh))
            return;

        MeshRenderer sourceRenderer = GetComponent<MeshRenderer>();
        if (!sourceRenderer)
        {
            EditorUtility.DisplayDialog("Bake Prefab", "The object does not have a MeshRenderer.", "OK");
            return;
        }

        GameObject temp = new GameObject(gameObject.name + "_Baked");

        try
        {
            temp.layer = gameObject.layer;
            temp.tag = gameObject.tag;
            temp.transform.position = Vector3.zero;
            temp.transform.rotation = Quaternion.identity;
            temp.transform.localScale = Vector3.one;

            GameObjectUtility.SetStaticEditorFlags(temp, GameObjectUtility.GetStaticEditorFlags(gameObject));

            MeshFilter bakedMeshFilter = temp.AddComponent<MeshFilter>();
            MeshRenderer bakedMeshRenderer = temp.AddComponent<MeshRenderer>();

            EditorUtility.CopySerialized(sourceRenderer, bakedMeshRenderer);
            bakedMeshFilter.sharedMesh = bakedMesh;
            bakedMeshRenderer.sharedMaterials = sourceRenderer.sharedMaterials;

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, prefabPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (prefab)
                SelectObjectDelayed(prefab);
            else
                EditorUtility.DisplayDialog("Bake Prefab", "Failed to save prefab.", "OK");
        }
        finally
        {
            DestroyImmediate(temp);
        }
    }
#endif

    public void Rebuild()
    {
        if (!this)
            return;

        EnsureMesh();
        if (_mesh == null || !_mesh || _meshFilter == null || !_meshFilter)
            return;

        if (points == null || points.Length < 2)
        {
            _mesh.Clear();
            return;
        }

        List<Vector3> sourcePoints = new List<Vector3>(points.Length);
        for (int i = 0; i < points.Length; i++)
        {
            if (points[i])
                sourcePoints.Add(transform.InverseTransformPoint(points[i].position));
        }

        if (sourcePoints.Count < 2)
        {
            _mesh.Clear();
            return;
        }

        List<Vector3> centers = pathMode == PathMode.CatmullRom
            ? BuildCatmullRom(sourcePoints, stepsPerSegment)
            : new List<Vector3>(sourcePoints);

        if (centers.Count < 2)
        {
            _mesh.Clear();
            return;
        }

        int ringVertexCount = sides + 1;
        int ringCount = centers.Count;

        List<Vector3> vertices = new List<Vector3>(ringCount * ringVertexCount);
        List<Vector3> normals = new List<Vector3>(ringCount * ringVertexCount);
        List<Vector2> uvs = new List<Vector2>(ringCount * ringVertexCount);
        List<int> triangles = new List<int>((ringCount - 1) * sides * 6 * (doubleSided ? 2 : 1));

        Vector3[] tangents = new Vector3[ringCount];
        for (int i = 0; i < ringCount; i++)
        {
            Vector3 tangent;

            if (i == 0)
                tangent = centers[1] - centers[0];
            else if (i == ringCount - 1)
                tangent = centers[ringCount - 1] - centers[ringCount - 2];
            else
                tangent = centers[i + 1] - centers[i - 1];

            if (tangent.sqrMagnitude < Epsilon)
                tangent = Vector3.forward;

            tangents[i] = tangent.normalized;
        }

        Vector3 normal = Vector3.up;
        normal = normal - tangents[0] * Vector3.Dot(normal, tangents[0]);

        if (normal.sqrMagnitude < Epsilon)
            normal = Vector3.right - tangents[0] * Vector3.Dot(Vector3.right, tangents[0]);

        normal.Normalize();

        float lengthU = 0f;

        for (int i = 0; i < ringCount; i++)
        {
            Vector3 center = centers[i];
            Vector3 tangent = tangents[i];

            if (i > 0)
            {
                lengthU += Vector3.Distance(centers[i - 1], centers[i]);

                Vector3 previousTangent = tangents[i - 1];
                Vector3 axis = Vector3.Cross(previousTangent, tangent);
                float axisLength = axis.magnitude;

                if (axisLength > 1e-6f)
                {
                    float dot = Mathf.Clamp(Vector3.Dot(previousTangent, tangent), -1f, 1f);
                    float angleDegrees = Mathf.Atan2(axisLength, dot) * Mathf.Rad2Deg;
                    normal = Quaternion.AngleAxis(angleDegrees, axis / axisLength) * normal;
                }

                normal = normal - tangent * Vector3.Dot(normal, tangent);

                if (normal.sqrMagnitude < Epsilon)
                    normal = Vector3.up - tangent * Vector3.Dot(Vector3.up, tangent);

                normal.Normalize();
            }

            Vector3 binormal = Vector3.Cross(tangent, normal);
            if (binormal.sqrMagnitude < Epsilon)
                binormal = Vector3.Cross(tangent, Vector3.up);

            binormal.Normalize();

            for (int side = 0; side < ringVertexCount; side++)
            {
                float t = (float)side / sides;
                float angle = t * Mathf.PI * 2f;

                Vector3 radial = binormal * Mathf.Cos(angle) + normal * Mathf.Sin(angle);
                Vector3 vertex = center + radial * radius;

                vertices.Add(vertex);
                normals.Add(radial);

                float v = flipV ? 1f - t : t;
                uvs.Add(new Vector2(lengthU * uvMultiply.x, v * uvMultiply.y));
            }
        }

        for (int ring = 0; ring < ringCount - 1; ring++)
        {
            int ringA = ring * ringVertexCount;
            int ringB = (ring + 1) * ringVertexCount;

            for (int side = 0; side < sides; side++)
            {
                int a = ringA + side;
                int a1 = ringA + side + 1;
                int b = ringB + side;
                int b1 = ringB + side + 1;

                if (!flipFaces)
                {
                    triangles.Add(a);
                    triangles.Add(a1);
                    triangles.Add(b);

                    triangles.Add(a1);
                    triangles.Add(b1);
                    triangles.Add(b);
                }
                else
                {
                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(a1);

                    triangles.Add(a1);
                    triangles.Add(b);
                    triangles.Add(b1);
                }

                if (doubleSided)
                {
                    int count = triangles.Count;

                    int i0 = triangles[count - 6];
                    int i1 = triangles[count - 5];
                    int i2 = triangles[count - 4];
                    int i3 = triangles[count - 3];
                    int i4 = triangles[count - 2];
                    int i5 = triangles[count - 1];

                    triangles.Add(i2);
                    triangles.Add(i1);
                    triangles.Add(i0);

                    triangles.Add(i5);
                    triangles.Add(i4);
                    triangles.Add(i3);
                }
            }
        }

        _mesh.Clear();
        _mesh.SetVertices(vertices);
        _mesh.SetNormals(normals);
        _mesh.SetUVs(0, uvs);
        _mesh.SetTriangles(triangles, 0, true);
        _mesh.RecalculateBounds();
        _mesh.RecalculateTangents();
    }

    static List<Vector3> BuildCatmullRom(List<Vector3> sourcePoints, int stepsPerSegment)
    {
        stepsPerSegment = Mathf.Max(1, stepsPerSegment);

        int count = sourcePoints.Count;
        List<Vector3> result = new List<Vector3>(count * stepsPerSegment);

        for (int i = 0; i < count - 1; i++)
        {
            Vector3 p0 = i == 0 ? sourcePoints[i] + (sourcePoints[i] - sourcePoints[i + 1]) : sourcePoints[i - 1];
            Vector3 p1 = sourcePoints[i];
            Vector3 p2 = sourcePoints[i + 1];
            Vector3 p3 = i + 2 < count ? sourcePoints[i + 2] : sourcePoints[i + 1] + (sourcePoints[i + 1] - sourcePoints[i]);

            for (int step = 0; step < stepsPerSegment; step++)
            {
                float t = (float)step / stepsPerSegment;
                result.Add(CatmullRom(p0, p1, p2, p3, t));
            }
        }

        result.Add(sourcePoints[count - 1]);
        return result;
    }

    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(ProceduralCableSkin))]
public class ProceduralCableSkinEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space();

        ProceduralCableSkin cable = (ProceduralCableSkin)target;

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Add Start"))
            cable.AddPointAtStartFromFirst();

        if (GUILayout.Button("Add"))
            cable.AddPointFromLast();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Bake Mesh"))
            cable.BakeMeshAsset();

        if (GUILayout.Button("Bake Prefab"))
            cable.BakePrefabAsset();
        EditorGUILayout.EndHorizontal();
    }
}
#endif