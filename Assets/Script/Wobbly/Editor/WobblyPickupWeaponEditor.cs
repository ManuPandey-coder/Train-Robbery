#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(WobblyPickupWeapon))]
public sealed class WobblyPickupWeaponEditor : Editor
{
    const string CopyKey = "Wobbly.GripPose.Copy.v1";
    const string CopyNameKey = "Wobbly.GripPose.CopyName.v1";
    bool showHandles = true;
    int selectedHand;
    int handleMode;
    WobblyWeaponController editingController;
    bool pausedByEditor;

    void OnEnable() { EditorApplication.update += UpdatePausedPreview; }

    void OnDisable()
    {
        EditorApplication.update -= UpdatePausedPreview;
        FinishEditing();
    }

    void UpdatePausedPreview()
    {
        if (!EditorApplication.isPlaying || !EditorApplication.isPaused || target == null) return;
        var weapon = target as WobblyPickupWeapon;
        if (weapon == null) return;
        var owner = Owner(weapon);
        if (owner == null || !owner.IsGripEditing) return;

        // A paused player does not run LateUpdate. Inspector text fields and
        // Scene handles can commit between GUI repaint events, so keep solving
        // the hands from the latest serialized values on every editor tick.
        // The gun Transform is also editable: its current pose becomes the new
        // lock pose before the wrists are solved, instead of being restored.
        owner.SetGripEditingGunPose(weapon.transform.position, weapon.transform.rotation);
        SceneView.RepaintAll();
        Repaint();
    }

    void FinishEditing()
    {
        if (editingController != null) editingController.EndGripEditing();
        editingController = null;
        if (pausedByEditor && EditorApplication.isPlaying) EditorApplication.isPaused = false;
        pausedByEditor = false;
    }

    static WobblyWeaponController Owner(WobblyPickupWeapon weapon)
    {
        var owner = weapon.GetComponentInParent<WobblyWeaponController>();
        return owner != null && owner.HeldWeapon == weapon ? owner : null;
    }

    static void RefreshPreview(WobblyPickupWeapon weapon)
    {
        var owner = Owner(weapon);
        if (owner != null) owner.RefreshGripPreview();
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
    }

    // Copy only authoring values. Never paste held state, physics, parenting,
    // collider references, or the gun's runtime world position back into a scene.
    [Serializable]
    public sealed class PoseCopy
    {
        public Vector3 primaryGripLocal;
        public Vector3 secondaryGripLocal;
        public float maximumHandSpacing;
        public bool limitHandSpacing;
        public bool configured = true;
        public bool independentHoldAnchor;
        public Vector3 holdAnchorLocal;
        public Vector3 holdPositionOffset;
        public Vector3 holdRotationOffset;
        public WobblyPickupWeapon.HandPose rightHandPose;
        public WobblyPickupWeapon.HandPose leftHandPose;
    }

    public override bool RequiresConstantRepaint() => Application.isPlaying;

    public override void OnInspectorGUI()
    {
        var weapon = (WobblyPickupWeapon)target;
        var owner = Owner(weapon);
        serializedObject.Update();
        EditorGUILayout.LabelField("Visual Grip Editor", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Play, pick up this gun, then select it under WobblyPlayer / HeldWeaponMount. " +
            "Click Lock gun & edit hands. This pauses the game and keeps the gun still. " +
            "Gun placement, hand positions, hand rotations and fingers update immediately, even while paused. " +
            "Scene view Gizmos must be on. Finish editing resumes only if this editor paused the game.", MessageType.Info);
        using (new EditorGUI.DisabledScope(owner == null || !owner.CanEditGrip))
        {
            if (GUILayout.Button(owner != null && owner.IsGripEditing ? "Finish editing hands" : "Lock gun & edit hands"))
            {
                if (owner.IsGripEditing) FinishEditing();
                else
                {
                    Undo.RecordObject(weapon, "Separate gun placement from hand grips");
                    if (owner.BeginGripEditing())
                    {
                        editingController = owner;
                        pausedByEditor = !EditorApplication.isPaused;
                        EditorApplication.isPaused = true;
                        serializedObject.Update();
                        RefreshPreview(weapon);
                    }
                }
            }
        }
        if (owner == null)
            EditorGUILayout.HelpBox("This is not a held gun. Settings are stored here, but a live hand preview needs the gun under HeldWeaponMount during Play.", MessageType.Info);
        showHandles = EditorGUILayout.Toggle("Show grip markers", showHandles);
        selectedHand = GUILayout.Toolbar(selectedHand, new[] { "Right hand (red)", "Left hand (cyan)" });
        handleMode = GUILayout.Toolbar(handleMode,
            new[] { "Move hand", "Rotate hand", "Move gun", "Rotate gun" });
        if (selectedHand == 1 && !weapon.UsesTwoHands)
            EditorGUILayout.HelpBox("This gun uses one hand. The left-hand pose is stored but not applied.", MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Copy pose (keeps after Play)"))
            {
                serializedObject.ApplyModifiedProperties();
                SessionState.SetString(CopyKey, CapturePose(weapon));
                SessionState.SetString(CopyNameKey, weapon.name);
            }
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(SessionState.GetString(CopyKey, ""))))
            {
                if (GUILayout.Button("Paste copied pose"))
                {
                    ApplyPose(weapon, SessionState.GetString(CopyKey, ""));
                    serializedObject.Update();
                }
            }
        }
        string copiedName = SessionState.GetString(CopyNameKey, "");
        if (!string.IsNullOrEmpty(copiedName))
            EditorGUILayout.HelpBox("Copied from: " + copiedName +
                ". Stop Play, select the original floor gun, Paste copied pose, then save the scene. " +
                "Only grip and hand settings are pasted.", MessageType.Info);

        DrawGunPlacement(weapon, owner);

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
        EditorGUI.BeginChangeCheck();
        using (new EditorGUI.DisabledScope(owner != null && !owner.IsGripEditing))
        {
            DrawPropertiesExcluding(serializedObject, "m_Script", "rightHandPose", "leftHandPose", "configured",
                "independentHoldAnchor", "holdAnchorLocal", "holdPositionOffset", "holdRotationOffset",
                "primaryGripLocal", "secondaryGripLocal", "maximumHandSpacing", "limitHandSpacing");
            DrawHand("rightHandPose", "primaryGripLocal", "Right hand", owner, true);
            using (new EditorGUI.DisabledScope(!weapon.UsesTwoHands))
                DrawHand("leftHandPose", "secondaryGripLocal", "Left hand", owner, false);
        }
        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.FindProperty("configured").boolValue = true;
            serializedObject.ApplyModifiedProperties();
            RefreshPreview(weapon);
        }
        else serializedObject.ApplyModifiedProperties();
        if (owner != null && owner.IsGripEditing && (owner.GripReachError(true) > .015f ||
            (weapon.UsesTwoHands && owner.GripReachError(false) > .015f)))
            EditorGUILayout.HelpBox("A grip is beyond this character's arm reach. Move its marker closer. " +
                "The arm stops at its natural length; the gun will not move to hide the error.", MessageType.Warning);

        Vector3 authored = serializedObject.FindProperty("secondaryGripLocal").vector3Value;
        if (weapon.UsesTwoHands && (authored - weapon.SecondaryGripLocal).sqrMagnitude > 0.000001f)
            EditorGUILayout.HelpBox("Maximum Hand Spacing is moving the left grip toward the right. " +
                "The filled cyan marker shows the actual target; the wire marker shows your authored point. " +
                "Increase the spacing or turn off Limit Hand Spacing for exact placement.", MessageType.Warning);
    }

    void DrawGunPlacement(WobblyPickupWeapon weapon, WobblyWeaponController owner)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Gun placement", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(owner == null || !owner.IsGripEditing))
        {
            EditorGUI.BeginChangeCheck();
            Vector3 position = EditorGUILayout.Vector3Field("Position", weapon.transform.position);
            Vector3 rotation = EditorGUILayout.Vector3Field("Rotation", weapon.transform.eulerAngles);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(weapon.transform, "Move locked gun");
                weapon.transform.SetPositionAndRotation(position, Quaternion.Euler(rotation));
                owner.SetGripEditingGunPose(position, Quaternion.Euler(rotation));
                EditorUtility.SetDirty(weapon.transform);
                SceneView.RepaintAll();
            }
        }
        if (owner == null || !owner.IsGripEditing)
            EditorGUILayout.HelpBox("Lock gun & edit hands to change the held gun placement.", MessageType.None);
    }

    void DrawHand(string propertyName, string positionName, string label, WobblyWeaponController owner, bool right)
    {
        SerializedProperty hand = serializedObject.FindProperty(propertyName);
        hand.isExpanded = EditorGUILayout.Foldout(hand.isExpanded, label, true);
        if (!hand.isExpanded) return;
        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(serializedObject.FindProperty(positionName), new GUIContent("Position"));
        EditorGUILayout.PropertyField(hand.FindPropertyRelative("wristRotation"), new GUIContent("Rotation"));
        SerializedProperty custom = hand.FindPropertyRelative("useCustomFingers");
        EditorGUILayout.PropertyField(custom);
        EditorGUI.BeginChangeCheck();
        string[] names = { "thumb", "index", "middle", "ring", "pinky" };
        for (int i = 0; i < names.Length; i++)
        {
            if (owner != null && owner.GetFingerBone(right, i, 0) == null) continue;
            SerializedProperty finger = hand.FindPropertyRelative(names[i]);
            finger.isExpanded = EditorGUILayout.Foldout(finger.isExpanded, ObjectNames.NicifyVariableName(names[i]), true);
            if (!finger.isExpanded) continue;
            EditorGUILayout.PropertyField(finger.FindPropertyRelative("baseCurl"));
            if (owner == null || owner.GetFingerBone(right, i, 1) != null)
                EditorGUILayout.PropertyField(finger.FindPropertyRelative("middleCurl"));
            if (owner == null || owner.GetFingerBone(right, i, 2) != null)
                EditorGUILayout.PropertyField(finger.FindPropertyRelative("tipCurl"));
            EditorGUILayout.PropertyField(finger.FindPropertyRelative("spread"));
            EditorGUILayout.PropertyField(finger.FindPropertyRelative("twist"));
        }
        if (EditorGUI.EndChangeCheck()) custom.boolValue = true;
        EditorGUILayout.HelpBox("Base / Middle / Tip Curl bend each joint. Spread opens the finger sideways; " +
            "Twist turns its base. Editing a slider enables custom fingers automatically. " +
            "During live editing, only fingers and joints found on the actual rig are shown.", MessageType.None);
        EditorGUI.indentLevel--;
    }

    void OnSceneGUI()
    {
        if (!showHandles) return;
        var weapon = (WobblyPickupWeapon)target;
        var owner = Owner(weapon);
        serializedObject.Update();
        DrawMarker(weapon.TransformGripPoint(weapon.PrimaryGripLocal), "RIGHT wrist", new Color(1f, .3f, .25f));
        if (weapon.UsesTwoHands)
        {
            Vector3 effective = weapon.TransformGripPoint(weapon.SecondaryGripLocal);
            Vector3 authored = weapon.TransformGripPoint(serializedObject.FindProperty("secondaryGripLocal").vector3Value);
            DrawMarker(effective, "LEFT wrist", Color.cyan);
            if (Vector3.Distance(effective, authored) > .001f)
            {
                Handles.color = Color.cyan;
                Handles.DrawDottedLine(effective, authored, 4f);
                Handles.DrawWireDisc(authored, SceneView.currentDrawingSceneView.camera.transform.forward,
                    HandleUtility.GetHandleSize(authored) * .035f);
            }
        }
        if (owner != null && !owner.IsGripEditing) return;
        if (handleMode >= 2)
        {
            if (owner == null || !owner.IsGripEditing) return;
            EditorGUI.BeginChangeCheck();
            Vector3 gunPosition = weapon.transform.position;
            Quaternion gunRotation = weapon.transform.rotation;
            if (handleMode == 2)
                gunPosition = Handles.PositionHandle(gunPosition, gunRotation);
            else
                gunRotation = Handles.RotationHandle(gunRotation, gunPosition);
            if (!EditorGUI.EndChangeCheck()) return;
            Undo.RecordObject(weapon.transform, handleMode == 2 ? "Move locked gun" : "Rotate locked gun");
            weapon.transform.SetPositionAndRotation(gunPosition, gunRotation);
            owner.SetGripEditingGunPose(gunPosition, gunRotation);
            EditorUtility.SetDirty(weapon.transform);
            SceneView.RepaintAll();
            Repaint();
            return;
        }
        if (selectedHand == 1 && !weapon.UsesTwoHands) return;
        bool right = selectedHand == 0;
        SerializedProperty point = serializedObject.FindProperty(right ? "primaryGripLocal" : "secondaryGripLocal");
        SerializedProperty rotation = serializedObject.FindProperty(right ? "rightHandPose" : "leftHandPose")
            .FindPropertyRelative("wristRotation");
        Vector3 position = weapon.TransformGripPoint(point.vector3Value);
        Handles.color = right ? new Color(1f, .3f, .25f) : Color.cyan;
        EditorGUI.BeginChangeCheck();
        if (handleMode == 0)
        {
            Vector3 moved = Handles.PositionHandle(position, weapon.transform.rotation);
            if (EditorGUI.EndChangeCheck())
                point.vector3Value = weapon.transform.InverseTransformPoint(moved);
            else return;
        }
        else
        {
            Quaternion rotated = Handles.RotationHandle(weapon.GripFrame(right), position);
            if (EditorGUI.EndChangeCheck())
                rotation.vector3Value = (Quaternion.Inverse(weapon.GripFrame(right, false)) * rotated).eulerAngles;
            else return;
        }
        // SerializedProperty writes support Undo and prefab overrides.
        serializedObject.FindProperty("configured").boolValue = true;
        serializedObject.ApplyModifiedProperties();
        RefreshPreview(weapon);
        Repaint();
    }

    static void DrawMarker(Vector3 position, string label, Color color)
    {
        Handles.color = color;
        float size = HandleUtility.GetHandleSize(position) * .045f;
        Handles.SphereHandleCap(0, position, Quaternion.identity, size, EventType.Repaint);
        Handles.Label(position + Vector3.up * size, label);
    }

    public static string CapturePose(WobblyPickupWeapon weapon)
    {
        var data = new SerializedObject(weapon);
        return JsonUtility.ToJson(new PoseCopy
        {
            primaryGripLocal = data.FindProperty("primaryGripLocal").vector3Value,
            secondaryGripLocal = data.FindProperty("secondaryGripLocal").vector3Value,
            maximumHandSpacing = data.FindProperty("maximumHandSpacing").floatValue,
            limitHandSpacing = data.FindProperty("limitHandSpacing").boolValue,
            independentHoldAnchor = data.FindProperty("independentHoldAnchor").boolValue,
            holdAnchorLocal = data.FindProperty("holdAnchorLocal").vector3Value,
            holdPositionOffset = data.FindProperty("holdPositionOffset").vector3Value,
            holdRotationOffset = data.FindProperty("holdRotationOffset").vector3Value,
            rightHandPose = weapon.RightHandPose,
            leftHandPose = weapon.LeftHandPose
        });
    }

    public static void ApplyPose(WobblyPickupWeapon weapon, string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        PoseCopy copy = JsonUtility.FromJson<PoseCopy>(json);
        if (copy == null || copy.rightHandPose == null || copy.leftHandPose == null) return;
        var data = new SerializedObject(weapon);
        data.Update();
        data.FindProperty("primaryGripLocal").vector3Value = copy.primaryGripLocal;
        data.FindProperty("secondaryGripLocal").vector3Value = copy.secondaryGripLocal;
        data.FindProperty("maximumHandSpacing").floatValue = copy.maximumHandSpacing;
        data.FindProperty("limitHandSpacing").boolValue = copy.limitHandSpacing;
        data.FindProperty("configured").boolValue = true;
        data.FindProperty("independentHoldAnchor").boolValue = copy.independentHoldAnchor;
        data.FindProperty("holdAnchorLocal").vector3Value = copy.holdAnchorLocal;
        data.FindProperty("holdPositionOffset").vector3Value = copy.holdPositionOffset;
        data.FindProperty("holdRotationOffset").vector3Value = copy.holdRotationOffset;
        WriteHand(data.FindProperty("rightHandPose"), copy.rightHandPose);
        WriteHand(data.FindProperty("leftHandPose"), copy.leftHandPose);
        data.ApplyModifiedProperties();
        PrefabUtility.RecordPrefabInstancePropertyModifications(weapon);
        EditorUtility.SetDirty(weapon);
        if (!Application.isPlaying && weapon.gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(weapon.gameObject.scene);
        RefreshPreview(weapon);
    }

    static void WriteHand(SerializedProperty property, WobblyPickupWeapon.HandPose pose)
    {
        property.FindPropertyRelative("wristRotation").vector3Value = pose.wristRotation;
        property.FindPropertyRelative("useCustomFingers").boolValue = pose.useCustomFingers;
        string[] names = { "thumb", "index", "middle", "ring", "pinky" };
        for (int i = 0; i < names.Length; i++)
        {
            SerializedProperty finger = property.FindPropertyRelative(names[i]);
            WobblyPickupWeapon.FingerPose angles = pose.Finger(i);
            finger.FindPropertyRelative("baseCurl").floatValue = angles.baseCurl;
            finger.FindPropertyRelative("middleCurl").floatValue = angles.middleCurl;
            finger.FindPropertyRelative("tipCurl").floatValue = angles.tipCurl;
            finger.FindPropertyRelative("spread").floatValue = angles.spread;
            finger.FindPropertyRelative("twist").floatValue = angles.twist;
        }
    }
}
#endif
