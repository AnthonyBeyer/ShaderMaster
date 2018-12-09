using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class ShaderMaster : EditorWindow
{
    private static class ContextMenuItem
    {
		#region Material

		private static Material mat;
		private static Material[] GetSelectedMaterials()
		{
			Material[] materials = Selection.GetFiltered<Material>(SelectionMode.DeepAssets);
			return materials;
		}

		[MenuItem("CONTEXT/Material/Toggle Editable", false, 0)]
        public static void ToggleEditable(MenuCommand command)
		{
            Material m = (Material)command.context;
            if (m.hideFlags == HideFlags.NotEditable)
                m.hideFlags = HideFlags.None;
            else m.hideFlags = HideFlags.NotEditable;

            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
		}
			
		[MenuItem("CONTEXT/Material/Copy Material + Shader", false, 0)]
        public static void DoMaterialCopy(MenuCommand command)
		{
            mat = (Material)command.context;
		}

		[MenuItem("CONTEXT/Material/Paste Material + Shader", false, 0)]
        public static void DoMaterialPaste(MenuCommand command)
		{
			if (mat == null) 
			{
				Debug.LogWarning ("No Material to paste, copy one material first.");
				return;
			}

            Material m = (Material)command.context;
            if (m != null && m != mat) 
            {
                m.shader = mat.shader;
                m.CopyPropertiesFromMaterial(mat);
            }
		}

		#endregion

		#region Shader

        private static Shader[] GetSelectedShaders()
        {
            Shader[] shaders = Selection.GetFiltered<Shader>(SelectionMode.DeepAssets);
            return shaders;
        }

        private static TextAsset[] GetSelectedCginc()
        {
            TextAsset[] textAssets = Selection.GetFiltered<TextAsset>(SelectionMode.DeepAssets);
            List<TextAsset> cgincs = new List<TextAsset>();
            for (int i=0; i<textAssets.Length; i++)
            {
                string path = AssetDatabase.GetAssetPath(textAssets[i]);
                if (path.ToLower().Contains(".cginc")) cgincs.Add(textAssets[i]);
            }
            return cgincs.ToArray();
        }

        [MenuItem("CONTEXT/Shader/Open in Shader Master", false, 0)]
        public static void Shader_OpenShaderMaster()
        {
            ShaderMaster.Open(GetSelectedShaders()[0]);
        }

        [MenuItem("CONTEXT/CGProgram/Open in Shader Master", false, 0)]
        public static void CGinc_OpenShaderMaster()
        {
            ShaderMaster.Open(GetSelectedCginc()[0]);
        }

		[MenuItem("Assets/Create/Shader/CgInc", false, 99)]
        public static void Create () 
        {
            string path = GetTargetPath(Selection.activeObject);
            string name = "NewCginc";
            string ext = ".cginc";
            string fullPath = AssetDatabase.GenerateUniqueAssetPath(path + name + ext);
            string content = "" +
                "#ifndef NEW_CGINC_INCLUDED \n" +
                "#define NEW_CGINC_INCLUDED \n" +
                " \n" + "// TODO : " + "Amazing CG \n" + " \n" +
                "#endif";

            File.WriteAllText(fullPath, content);
            AssetDatabase.ImportAsset(fullPath, ImportAssetOptions.ForceUpdate);

            EditorUtility.FocusProjectWindow();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<TextAsset>(fullPath);
        }

        static string GetTargetPath(Object obj)
        {
            string targetPath = "Assets/";
            if (obj == null) return targetPath;

            string objPath = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(objPath)) return targetPath;

            if (AssetDatabase.IsValidFolder(objPath)) targetPath = objPath + "/";
            else
            {
                targetPath = "";
                string[] splitPath = objPath.Split(new string[1]{"/"}, System.StringSplitOptions.None);
                for (int i=0; i<splitPath.Length-1; i++) targetPath += splitPath[i] + "/";
            }

            return targetPath;
        }

		#endregion
    }
		
    #region COMPILATION Forced

    Object compilationFolder;
    const string defaultFolder = "Assets";

    void DrawForceCompilation()
    {
        DrawHeader("Force Shaders Compilation");

        compilationFolder = FolderField(compilationFolder, "Folder");
        if (GUILayout.Button("Recompil all shaders in this folder"))
            ForceShaderCompilation();
    }

    void ForceShaderCompilation()
    {
        string folderPath = AssetDatabase.GetAssetPath(compilationFolder);
        if (string.IsNullOrEmpty(folderPath)) folderPath = defaultFolder;

        string dialogMessage = "Are you sure? This functionality writes all .shader files in the [ " +folderPath+ " ] folder and can take several minutes.";

        if (EditorUtility.DisplayDialog("Force Shaders Compilation", dialogMessage, "Of Course !", "Hum... nope."))
        {
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                Debug.LogError(folderPath + " is not a valid folder.");
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:Shader", new string[1]{folderPath});
            for (int i=0; i<guids.Length; i++)
            {
                string shaderPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);

                string marker = "//LastForcedCompilation";
                string date = System.DateTime.Now.Day.ToString() +"/"+ System.DateTime.Now.Month.ToString() +"/"+ System.DateTime.Now.Year.ToString();
                string time = System.DateTime.Now.Hour.ToString() +":"+ System.DateTime.Now.Minute.ToString() +":"+ System.DateTime.Now.Second.ToString();
                string append = marker +"_"+ date +"_"+ time;

                System.IO.FileStream str = System.IO.File.OpenRead(Application.dataPath.Replace("Assets","") + shaderPath);
                Debug.Log(str.Name, shader);
                str.Close();

                string[] lines = System.IO.File.ReadAllLines(Application.dataPath.Replace("Assets","") + shaderPath);

                if (lines[0].Contains(marker))
                {
                    lines[0] = append; 
                }
                else
                {
                    List<string> l = lines.ToList();
                    l.Insert(0, append); 
                    lines = l.ToArray();
                }

                System.IO.File.WriteAllLines(Application.dataPath.Replace("Assets","") + shaderPath, lines);
                AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceUpdate);
            }
        }
    }

    #endregion

    #region CGINC Occurence

    TextAsset cginc;
    TextAsset lastCginc;
    List<string> cgincs = new List<string>();
    Vector2 scrollShaders;

    void DrawCgincOccurence()
    {
        DrawHeader("Find all shaders/cginc using this cginc", ref cginc);

        if (cginc != lastCginc)
        {
            if (cginc == null)
                cgincs.Clear();
            else
            {
                List<string> all = new List<string>();

                string[] allTextAsset = AssetDatabase.FindAssets("t:TextAsset");
                for (int i = 0; i < allTextAsset.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(allTextAsset[i]);
                    if (path.Contains(".cginc")) all.Add(allTextAsset[i]);
                }
                string[] allShaders = AssetDatabase.FindAssets("t:Shader");
                for (int i = 0; i < allShaders.Length; i++) all.Add(allShaders[i]);

                cgincs.Clear();
                for (int i = 0; i < all.Count; i++)
                {
                    all[i] = AssetDatabase.GUIDToAssetPath(all[i]);
                    if (!string.IsNullOrEmpty(all[i]))
                    {
                        string text = System.IO.File.ReadAllText(Application.dataPath.Replace("Assets","") + all[i]);
                        text = text.ToLower();
                        if (text.Contains(cginc.name.ToLower() + ".cginc")) cgincs.Add(all[i]);
                    }
                }
            }
            lastCginc = cginc;
        }

        DrawScrollView(ref scrollShaders, cgincs);

        DrawHorizontalLine();
    }

    #endregion

    #region SHADER Occurence

    Shader shader;
    Shader lastShader;
    List<string> materials = new List<string>();
    Vector2 scrollMaterials;

    void DrawShaderOccurence()
    {
        DrawHeader("Find all materials using this shader", ref shader);

        if (shader != lastShader)
        {
            if (shader == null)
                materials.Clear();
            else
            {
                string shaderPath = AssetDatabase.GetAssetPath(shader);
                string[] allMaterials = AssetDatabase.FindAssets("t:Material");
                materials.Clear();
                for (int i = 0; i < allMaterials.Length; i++)
                {
                    allMaterials[i] = AssetDatabase.GUIDToAssetPath(allMaterials[i]);
                    string[] dep = AssetDatabase.GetDependencies(allMaterials[i]);
                    if (ArrayUtility.Contains(dep, shaderPath))
                        materials.Add(allMaterials[i]);
                }
            }
            lastShader = shader;
        }

        DrawScrollView(ref scrollMaterials, materials);

        DrawHorizontalLine();
    }

    #endregion

    #region MaterialKeywords

    Material matK;

    void DrawMaterialKeywords()
    {
        DrawHeader("Material Keywords", ref matK);

        if (matK != null)
        {
            if (matK.shaderKeywords != null)
            {
                for (int i=0; i<matK.shaderKeywords.Length; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(matK.shaderKeywords[i]);
                    if (matK.IsKeywordEnabled(matK.shaderKeywords[i]))
                    {
                        if (GUILayout.Button("Disable Keyword"))
                            matK.DisableKeyword(matK.shaderKeywords[i]);
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }
        }

        DrawHorizontalLine();
    }

    #endregion

    #region GlobalKeywords

    string globalKeyword;

    void DrawGlobalKeywords()
    {
        DrawHeader("Global Keywords");

        globalKeyword = EditorGUILayout.TextField("Global Keyword", globalKeyword);

        if (Shader.IsKeywordEnabled(globalKeyword))
        {
            if (GUILayout.Button("Disable Global Keyword"))
                Shader.DisableKeyword(globalKeyword);
        }
        else
        {
            if (GUILayout.Button("Enable Global Keyword"))
                Shader.EnableKeyword(globalKeyword);
        }

        DrawHorizontalLine();
    }

    #endregion

	[MenuItem("Tools/Shader Master")]
	public static void Open()
	{
		GetWindow<ShaderMaster>();
	}

    public static void Open(TextAsset cg)
    {
        ShaderMaster shaderMaster = GetWindow<ShaderMaster>();
        shaderMaster.cginc = cg;
    }

    public static void Open(Shader s)
    {
        ShaderMaster shaderMaster = GetWindow<ShaderMaster>();
        shaderMaster.shader = s;
    }

	void OnGUI()
	{
		DrawForceCompilation();
        EditorGUILayout.Space();

		DrawCgincOccurence();
        EditorGUILayout.Space();
		DrawShaderOccurence();
        EditorGUILayout.Space();

        DrawGlobalKeywords();
        EditorGUILayout.Space();
        DrawMaterialKeywords();
	}

    #region GUI Helpers

    void DrawScrollView(ref Vector2 scroll, List<string> paths)
    {
        scroll = GUILayout.BeginScrollView(scroll);
        {
            for (int i = 0; i < paths.Count; i++)
            {
                GUILayout.BeginHorizontal();
                {
                    Object asset = AssetDatabase.LoadAssetAtPath<Object>(paths[i]);
                    Texture2D assetPreview = AssetPreview.GetMiniThumbnail(asset);
                    string assetName = Path.GetFileName(paths[i]);
                    GUIContent content = new GUIContent(assetPreview);

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label(content, GUILayout.Height(20), GUILayout.Width(20));
                    GUILayout.Label(assetName, GUILayout.Height(20));

                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Show"))
                    {
                        EditorGUIUtility.PingObject(asset);
                    }
                    EditorGUILayout.EndHorizontal();

                }
                GUILayout.EndHorizontal();
            }
        }
        GUILayout.EndScrollView();
    }

    Object FolderField(Object folder, string name)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(name);

        folder = EditorGUILayout.ObjectField(folder, typeof(Object), false) as Object;
        if (folder != null)
        {
            if (!IsValidFolder(folder))
                folder = null;
        }
        GUILayout.EndHorizontal();

        return folder;
    }

    bool IsValidFolder(Object folder)
    {
        if (folder == null) // no folder
            return false;

        string folderPath = AssetDatabase.GetAssetPath(folder);
        if (!AssetDatabase.IsValidFolder(folderPath)) // not a folder
            return false; 

        return true;
    }

    void BeginHeader(string title)
    {
        EditorGUILayout.BeginVertical("Box");
        EditorGUILayout.BeginVertical("Box");
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.Label (title, EditorStyles.largeLabel);
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    void DrawHeader(string title)
    {
        BeginHeader(title);
        EditorGUILayout.EndVertical();
        EditorGUILayout.EndVertical();
    }

    void DrawHeader(string title, ref Material m)
    {
        BeginHeader(title);
        m = EditorGUILayout.ObjectField(m, typeof(Material), false) as Material;
        EditorGUILayout.EndVertical();
        EditorGUILayout.EndVertical();
    }

    void DrawHeader(string title, ref Shader s)
    {
        BeginHeader(title);
        s = EditorGUILayout.ObjectField(s, typeof(Shader), false) as Shader;
        EditorGUILayout.EndVertical();
        EditorGUILayout.EndVertical();
    }

    void DrawHeader(string title, ref TextAsset t)
    {
        BeginHeader(title);
        t = EditorGUILayout.ObjectField(t, typeof(TextAsset), false) as TextAsset;
        if (t != null)
        {
            if (!AssetDatabase.GetAssetPath(t).ToLower().Contains(".cginc"))
                t = null;
        }
        EditorGUILayout.EndVertical();
        EditorGUILayout.EndVertical();
    }

    void DrawHorizontalLine()
    {
        GUIStyle s = new GUIStyle(GUI.skin.horizontalSlider);
        s.fixedHeight = 10;
        EditorGUILayout.LabelField("", s, GUILayout.Height(8));
    }

    #endregion

}