#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class RobberWeaponSetup
{
    private const string ModelPath="Assets/meshes/robber mangl.fbx";
    private const string PlayerPrefab="Assets/Prefabs/Player/WobblyPlayer.prefab";

    [MenuItem("Tools/Game Setup/Apply New Robber And Weapons")]
    public static void Apply()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        Scene scene=SceneManager.GetActiveScene();
        if(scene.path!="Assets/Scenes/game.unity") throw new InvalidOperationException("Open the game scene first.");
        ModelImporter importer=AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if(importer!=null && !importer.isReadable){importer.isReadable=true;importer.SaveAndReimport();}
        GameObject model=AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        PlayerController player=UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        if(model==null || player==null) throw new InvalidOperationException("The robber model or scene player is missing.");
        Undo.RegisterFullObjectHierarchyUndo(player.gameObject,"Apply new robber and guns");
        if(PrefabUtility.IsPartOfPrefabInstance(player.gameObject))
            PrefabUtility.UnpackPrefabInstance(player.gameObject,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
        Transform old=player.transform.Find("RobberVisualRig");
        RuntimeAnimatorController controller=old!=null ? old.GetComponent<Animator>()?.runtimeAnimatorController : null;
        if(controller==null) controller=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/player animation/Locomotion/player.controller");
        Vector3 position=old!=null ? old.localPosition : Vector3.zero;
        Quaternion rotation=old!=null ? old.localRotation : Quaternion.identity;
        Vector3 scale=old!=null ? old.localScale : Vector3.one;
        if(old!=null) Undo.DestroyObjectImmediate(old.gameObject);
        Transform physics=player.transform.Find("Physics");
        if(physics!=null) Undo.DestroyObjectImmediate(physics.gameObject);
        GameObject visual=(GameObject)PrefabUtility.InstantiatePrefab(model,player.transform);
        visual.name="RobberVisualRig";
        visual.transform.SetLocalPositionAndRotation(position,rotation);
        visual.transform.localScale=scale;
        // Unpack only the scene copy, allowing muzzle markers and activation
        // overrides without changing the Blender import or its gun placement.
        PrefabUtility.UnpackPrefabInstance(visual,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
        foreach(Transform t in visual.GetComponentsInChildren<Transform>(true))
        {
            if(t.name=="eyes" || t.name=="HAT.001" || t.name=="upper.001" || t.name=="upper.002") t.gameObject.SetActive(false);
        }
        foreach(MeshFilter filter in visual.GetComponentsInChildren<MeshFilter>(true).Where(m=>m.name.StartsWith("Gun_",StringComparison.Ordinal)))
        {
            GunDefinition gun=filter.GetComponent<GunDefinition>();
            if(gun==null) gun=filter.gameObject.AddComponent<GunDefinition>();
            Transform muzzle=filter.transform.Find("Muzzle");
            if(muzzle==null){muzzle=new GameObject("Muzzle").transform;muzzle.SetParent(filter.transform,false);}
            Bounds bounds=filter.sharedMesh.bounds;
            Vector3[] front=filter.sharedMesh.vertices.Where(v=>v.y>=bounds.max.y-bounds.size.y*.04f).ToArray();
            Vector3 tip=front.Length>0 ? front.Aggregate(Vector3.zero,(sum,v)=>sum+v)/front.Length : bounds.center;
            tip.y=bounds.max.y;
            muzzle.localPosition=tip;muzzle.localRotation=Quaternion.LookRotation(Vector3.up,Vector3.back);
            gun.Configure(muzzle);
            gun.gameObject.SetActive(gun.name.StartsWith("Gun_01_",StringComparison.Ordinal));
        }
        Animator animator=visual.GetComponent<Animator>();
        if(animator==null) animator=visual.AddComponent<Animator>();
        animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;
        animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        SerializedObject pc=new SerializedObject(player);
        pc.FindProperty("movementAnimator").objectReferenceValue=animator;
        pc.ApplyModifiedPropertiesWithoutUndo();
        WeaponController weapons=player.GetComponent<WeaponController>();
        if(weapons==null) weapons=player.gameObject.AddComponent<WeaponController>();
        var serializedWeapons=new SerializedObject(weapons);
        GunDefinition[] guns=visual.GetComponentsInChildren<GunDefinition>(true).OrderBy(g=>g.name,StringComparer.Ordinal).ToArray();
        var array=serializedWeapons.FindProperty("guns");array.arraySize=guns.Length;
        for(int i=0;i<guns.Length;i++) array.GetArrayElementAtIndex(i).objectReferenceValue=guns[i];
        serializedWeapons.FindProperty("selectedWeapon").intValue=0;
        serializedWeapons.FindProperty("startEquipped").boolValue=true;
        serializedWeapons.ApplyModifiedPropertiesWithoutUndo();
        SetWeaponMaterials(player);
        PrefabUtility.SaveAsPrefabAsset(player.gameObject,PlayerPrefab);
        MenuManager menu=UnityEngine.Object.FindFirstObjectByType<MenuManager>();
        if(menu!=null){Undo.RegisterFullObjectHierarchyUndo(menu.gameObject,"Repair customization layout");menu.RepairCustomizationLayout();EditorUtility.SetDirty(menu);}
        EditorUtility.SetDirty(player);
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        Debug.Log("New robber applied: "+guns.Length+" authored hand guns; bullets and laser weapons configured.");
    }

    [MenuItem("Tools/Game Setup/Repair Robber Weapon Materials")]
    public static void RepairWeaponMaterials()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        PlayerController player=UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        SetWeaponMaterials(player);
        PrefabUtility.SaveAsPrefabAsset(player.gameObject,PlayerPrefab);
        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        EditorSceneManager.SaveScene(player.gameObject.scene);
        AssetDatabase.SaveAssets();
    }

    private static void SetWeaponMaterials(PlayerController player)
    {
        if(!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets","Materials");
        if(!AssetDatabase.IsValidFolder("Assets/Materials/Weapons")) AssetDatabase.CreateFolder("Assets/Materials","Weapons");
        foreach(GunDefinition gun in player.GetComponentsInChildren<GunDefinition>(true))
        {
            bool future=gun.Type==GunDefinition.FireType.Laser;
            string path="Assets/Materials/Weapons/"+(future?"Energy Weapon":"Gunmetal")+".mat";
            Material material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null)
            {
                material=new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.SetColor("_BaseColor",future?new Color(.10f,.15f,.20f):new Color(.19f,.21f,.24f));
                material.SetFloat("_Metallic",.55f);material.SetFloat("_Smoothness",.25f);
                material.DisableKeyword("_EMISSION");material.SetColor("_EmissionColor",Color.black);
                AssetDatabase.CreateAsset(material,path);
            }
            Renderer renderer=gun.GetComponent<Renderer>();
            renderer.sharedMaterials=Enumerable.Repeat(material,renderer.sharedMaterials.Length).ToArray();
        }
    }

    [MenuItem("Tools/Game Setup/Repair Train Cars")]
    public static void RepairTrain()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        GameObject model=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/meshes/TRAIN.fbx");
        TrainManager manager=UnityEngine.Object.FindFirstObjectByType<TrainManager>();
        if(model==null || manager==null) throw new InvalidOperationException("Train model or manager missing.");
        Transform Find(string name)=>model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name.Trim().Equals(name,StringComparison.OrdinalIgnoreCase));
        GameObject engine=CarPrefab(Find("engine"),"Train Engine");
        GameObject passenger=CarPrefab(Find("passenger"),"Passenger Car");
        GameObject storage=CarPrefab(Find("storage"),"Storage Car");
        GameObject last=CarPrefab(Find("last") ?? Find("storage"),"Last Car");
        Undo.RecordObject(manager,"Repair train consist");
        var so=new SerializedObject(manager);
        so.FindProperty("trainModel").objectReferenceValue=model;
        so.FindProperty("enginePrefab").objectReferenceValue=engine;
        so.FindProperty("lastWagonPrefab").objectReferenceValue=last;
        var wagons=so.FindProperty("wagonPrefabs");wagons.arraySize=2;
        wagons.GetArrayElementAtIndex(0).objectReferenceValue=passenger;
        wagons.GetArrayElementAtIndex(1).objectReferenceValue=storage;
        so.FindProperty("wagonCount").intValue=5;
        so.FindProperty("engineYawCorrection").floatValue=0f;
        so.FindProperty("wagonYawCorrection").floatValue=0f;
        so.FindProperty("lastWagonYawCorrection").floatValue=180f;
        var transforms=UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None);
        Transform start=transforms.FirstOrDefault(t=>t.name=="Route Start - Behind Tunnel");
        Transform end=transforms.FirstOrDefault(t=>t.name=="Route End - After Bridge");
        if(start==null || end==null) throw new InvalidOperationException("Existing train route markers missing.");
        so.FindProperty("routeStart").objectReferenceValue=start;
        so.FindProperty("routeEnd").objectReferenceValue=end;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(manager);
        EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        EditorSceneManager.SaveScene(manager.gameObject.scene);AssetDatabase.SaveAssets();
        Debug.Log("Train repaired: engine + five visible cars, dedicated last car, original route preserved.");
    }

    private static GameObject CarPrefab(Transform source,string name)
    {
        if(source==null) throw new InvalidOperationException("Missing train geometry for "+name);
        GameObject root=new GameObject(name);
        try
        {
            GameObject visual=UnityEngine.Object.Instantiate(source.gameObject,root.transform,false);
            visual.name=source.name.Trim();
            visual.transform.localPosition=source.localPosition;
            visual.transform.localRotation=source.localRotation;
            visual.transform.localScale=source.localScale;
            Renderer[] renderers=visual.GetComponentsInChildren<Renderer>(true);
            if(renderers.Length==0) throw new InvalidOperationException("No geometry for "+name);
            Bounds bounds=renderers[0].bounds;
            foreach(Renderer renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            visual.transform.localPosition-=new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
            return PrefabUtility.SaveAsPrefabAsset(root,"Assets/Prefabs/Train/"+name+".prefab");
        }
        finally{UnityEngine.Object.DestroyImmediate(root);}
    }
}
#endif
