using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class WobblyTrainManager : MonoBehaviour
{
    [Header("Schedule")]
    [Tooltip("Start the repeating train service when Play Mode begins.")]
    [SerializeField] private bool trainsEnabled = true;
    [Tooltip("Seconds before the first train arrives. The following trains use Repeat Interval.")]
    [SerializeField, Min(0f)] private float firstTrainDelay = 8f;
    [Tooltip("Time between train departures in seconds. 300 seconds is five minutes.")]
    [SerializeField, Min(1f)] private float repeatInterval = 300f;
    [Tooltip("Allow another train to leave on schedule if the previous train is still in the scene.")]
    [SerializeField] private bool allowOverlappingTrains = true;

    [Header("Route")]
    [Tooltip("The front of the engine starts here, just outside the back of the tunnel.")]
    [SerializeField] private Transform routeStart;
    [Tooltip("The train is removed only after its last wagon passes this point beyond the bridge.")]
    [SerializeField] private Transform routeEnd;
    [Tooltip("Cruising speed in metres per second.")]
    [SerializeField, Min(0.1f)] private float travelSpeed = 12f;
    [Tooltip("How gently the train builds up to its cruising speed.")]
    [SerializeField, Min(0.01f)] private float acceleration = 1.8f;

    [Header("Train Consist")]
    [Tooltip("Imported TRAIN model containing the engine and wagon meshes.")]
    [SerializeField] private GameObject trainModel;
    [SerializeField] private string enginePartName = "engine";
    [Tooltip("Names of model parts that can be randomly selected for each wagon.")]
    [SerializeField] private string[] wagonPartNames = { "passenger", "STORAGE" };
    [Tooltip("This model part is always used for the final wagon.")]
    [SerializeField] private string lastWagonPartName = "last";
    [Tooltip("Total wagons behind the engine. The final slot is always reserved for the last wagon.")]
    [SerializeField, Range(1, 20)] private int wagonCount = 5;
    [Tooltip("Uniform scale applied to every engine and wagon model.")]
    [SerializeField, Min(0.01f)] private float trainScale = 2.37f;
    [Tooltip("Yaw correction for the imported engine mesh.")]
    [SerializeField, Range(-180f, 180f)] private float engineYawCorrection = 180f;
    [Tooltip("Yaw correction for passenger and storage wagons.")]
    [SerializeField, Range(-180f, 180f)] private float wagonYawCorrection = 180f;
    [Tooltip("The final wagon faces away from the direction of travel.")]
    [SerializeField, Range(-180f, 180f)] private float lastWagonYawCorrection;
    [Tooltip("Clear space between neighbouring cars.")]
    [SerializeField, Min(0f)] private float couplingGap = 0.65f;
    [Tooltip("Small vertical clearance above the rails.")]
    [SerializeField, Min(0f)] private float railClearance = 0.08f;
    [Tooltip("Extra distance beyond the end marker before the train is removed.")]
    [SerializeField, Min(0f)] private float despawnPadding = 2f;
    [Tooltip("Use zero for a different consist each run, or a fixed value for repeatable wagon choices.")]
    [SerializeField] private int randomSeed;

    private readonly List<ActiveTrain> activeTrains = new List<ActiveTrain>();
    private float departureTimer;
    private int departureNumber;

    public float SecondsUntilNextTrain => Mathf.Max(0f, departureTimer);
    public int ActiveTrainCount => activeTrains.Count;

    private void OnEnable()
    {
        departureTimer = Mathf.Max(0f, firstTrainDelay);
    }

    private void Update()
    {
        RemoveDestroyedTrains();
        if (!trainsEnabled || trainModel == null || routeStart == null || routeEnd == null) return;

        departureTimer -= Time.deltaTime;
        if (departureTimer > 0f) return;

        if (allowOverlappingTrains || activeTrains.Count == 0)
        {
            SpawnTrainNow();
            departureTimer = Mathf.Max(1f, repeatInterval);
        }
        else
        {
            departureTimer = 1f;
        }
    }

    private void FixedUpdate()
    {
        float deltaTime = Time.fixedDeltaTime;
        for (int i = activeTrains.Count - 1; i >= 0; i--)
        {
            ActiveTrain train = activeTrains[i];
            if (train.Root == null)
            {
                activeTrains.RemoveAt(i);
                continue;
            }

            train.Speed = Mathf.MoveTowards(train.Speed, travelSpeed, acceleration * deltaTime);
            train.Distance += train.Speed * deltaTime;

            float routeLength = train.RouteLength;
            Vector3 position;
            if (train.Distance <= routeLength)
            {
                float routeProgress = routeLength <= 0.001f ? 1f : train.Distance / routeLength;
                position = Vector3.LerpUnclamped(train.Start, train.End, routeProgress);
            }
            else
            {
                position = train.End + train.Direction * (train.Distance - routeLength);
            }

            train.Body.MovePosition(position);
            if (train.Distance < routeLength + train.ConsistLength + despawnPadding) continue;

            Destroy(train.Root);
            activeTrains.RemoveAt(i);
        }
    }

    [ContextMenu("Spawn Train Now")]
    public void SpawnTrainNow()
    {
        if (trainModel == null || routeStart == null || routeEnd == null)
        {
            Debug.LogWarning("Train Manager needs a train model and both route markers.", this);
            return;
        }

        Vector3 routeVector = routeEnd.position - routeStart.position;
        float routeLength = routeVector.magnitude;
        if (routeLength < 0.1f)
        {
            Debug.LogWarning("Train route markers are too close together.", this);
            return;
        }

        Vector3 direction = routeVector / routeLength;
        Vector3 flatDirection = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (flatDirection.sqrMagnitude < 0.001f) flatDirection = Vector3.forward;

        GameObject consist = new GameObject($"Passing Train {++departureNumber:00}");
        consist.transform.SetPositionAndRotation(routeStart.position,
            Quaternion.LookRotation(flatDirection.normalized, Vector3.up));
        consist.AddComponent<WobblyClimbableSurface>();

        Rigidbody body = consist.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        System.Random random = randomSeed == 0
            ? new System.Random(unchecked(Environment.TickCount + departureNumber * 7919))
            : new System.Random(unchecked(randomSeed + departureNumber * 7919));

        float rearEdge = AddCar(consist.transform, enginePartName, "Engine", 0f, true,
            engineYawCorrection);
        for (int i = 0; i < wagonCount; i++)
        {
            bool finalWagon = i == wagonCount - 1;
            string partName = finalWagon ? lastWagonPartName : PickWagonName(random);
            float yawCorrection = finalWagon ? lastWagonYawCorrection : wagonYawCorrection;
            rearEdge = AddCar(consist.transform, partName,
                $"Wagon {i + 1:00} - {partName}", rearEdge, false, yawCorrection);
        }

        float consistLength = Mathf.Max(1f, -rearEdge);
        activeTrains.Add(new ActiveTrain
        {
            Root = consist,
            Body = body,
            Start = routeStart.position,
            End = routeEnd.position,
            Direction = direction,
            RouteLength = routeLength,
            ConsistLength = consistLength,
            Distance = 0f,
            Speed = 0f
        });
        int meshColliderCount = consist.GetComponentsInChildren<MeshCollider>(true).Length;
        int boxColliderCount = consist.GetComponentsInChildren<BoxCollider>(true).Length;
        Debug.Log($"PASSING_TRAIN_DEPARTED: engine + {Mathf.Max(0, wagonCount - 1)} random + last; " +
                  $"{meshColliderCount} mesh colliders, {boxColliderCount} box colliders, " +
                  $"speed {travelSpeed:0.0} m/s.", this);
    }

    private float AddCar(
        Transform consist, string partName, string carName, float previousRearEdge,
        bool firstCar, float yawCorrection)
    {
        Transform sourcePart = FindPart(trainModel.transform, partName);
        if (sourcePart == null)
        {
            Debug.LogWarning($"Train model part '{partName}' was not found. Using the first visible part.", this);
            sourcePart = FirstRenderablePart(trainModel.transform);
        }

        GameObject car = new GameObject(carName);
        car.transform.SetParent(consist, false);
        car.transform.localPosition = Vector3.zero;
        car.transform.localRotation = Quaternion.Euler(0f, yawCorrection, 0f);
        car.transform.localScale = Vector3.one * trainScale;

        GameObject visual = Instantiate(sourcePart.gameObject, car.transform, false);
        visual.name = partName + " Visual";
        visual.transform.localPosition = sourcePart.localPosition;
        visual.transform.localRotation = sourcePart.localRotation;
        visual.transform.localScale = sourcePart.localScale;
        DisableImportedBehaviour(visual);
        ConfigureDoubleSidedMaterials(visual);

        Bounds initialBounds = CalculateBoundsRelativeTo(consist, car);
        float targetFrontEdge = firstCar ? 0f : previousRearEdge - couplingGap;
        Vector3 localPosition = car.transform.localPosition;
        localPosition.z += targetFrontEdge - initialBounds.max.z;
        localPosition.y += railClearance - initialBounds.min.y;
        car.transform.localPosition = localPosition;

        AddMeshColliders(visual);

        Bounds placedBounds = CalculateBoundsRelativeTo(consist, car);
        return placedBounds.min.z;
    }

    private string PickWagonName(System.Random random)
    {
        if (wagonPartNames == null || wagonPartNames.Length == 0) return "passenger";
        for (int attempt = 0; attempt < wagonPartNames.Length; attempt++)
        {
            string candidate = wagonPartNames[random.Next(0, wagonPartNames.Length)];
            if (!string.IsNullOrWhiteSpace(candidate)) return candidate.Trim();
        }
        return "passenger";
    }

    private static Transform FindPart(Transform root, string partName)
    {
        if (root == null || string.IsNullOrWhiteSpace(partName)) return null;
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
            if (string.Equals(transforms[i].name, partName, StringComparison.OrdinalIgnoreCase))
                return transforms[i];
        return null;
    }

    private static Transform FirstRenderablePart(Transform root)
    {
        Renderer renderer = root.GetComponentInChildren<Renderer>(true);
        return renderer != null ? renderer.transform : root;
    }

    private static void DisableImportedBehaviour(GameObject root)
    {
        Collider[] importedColliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < importedColliders.Length; i++) importedColliders[i].enabled = false;
        Animator[] animators = root.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++) animators[i].enabled = false;
        Animation[] animations = root.GetComponentsInChildren<Animation>(true);
        for (int i = 0; i < animations.Length; i++) animations[i].enabled = false;
    }

    private static void AddMeshColliders(GameObject root)
    {
        MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < filters.Length; i++)
        {
            MeshFilter filter = filters[i];
            if (filter.sharedMesh == null) continue;
            MeshCollider collider = filter.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
            collider.convex = false;
            collider.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation |
                                      MeshColliderCookingOptions.EnableMeshCleaning |
                                      MeshColliderCookingOptions.WeldColocatedVertices |
                                      MeshColliderCookingOptions.UseFastMidphase;
        }
    }

    private static void ConfigureDoubleSidedMaterials(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            Material[] materials = renderers[rendererIndex].materials;
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                Material material = materials[materialIndex];
                if (material == null) continue;
                material.doubleSidedGI = true;
                if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);
                if (material.HasProperty("_CullMode")) material.SetFloat("_CullMode", 0f);
                if (material.HasProperty("_DoubleSidedEnable")) material.SetFloat("_DoubleSidedEnable", 1f);
                material.EnableKeyword("_DOUBLESIDED_ON");
            }
            renderers[rendererIndex].materials = materials;
        }
    }

    private static Bounds CalculateBoundsRelativeTo(Transform relativeTo, GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(false);
        bool initialized = false;
        Bounds result = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            Bounds sourceBounds = filter != null && filter.sharedMesh != null
                ? filter.sharedMesh.bounds
                : renderer.localBounds;
            Matrix4x4 matrix = relativeTo.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            Vector3 min = sourceBounds.min;
            Vector3 max = sourceBounds.max;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = matrix.MultiplyPoint3x4(new Vector3(
                    (corner & 1) == 0 ? min.x : max.x,
                    (corner & 2) == 0 ? min.y : max.y,
                    (corner & 4) == 0 ? min.z : max.z));
                if (!initialized)
                {
                    result = new Bounds(point, Vector3.zero);
                    initialized = true;
                }
                else result.Encapsulate(point);
            }
        }
        return initialized ? result : new Bounds(Vector3.zero, Vector3.one);
    }

    private void RemoveDestroyedTrains()
    {
        for (int i = activeTrains.Count - 1; i >= 0; i--)
            if (activeTrains[i].Root == null) activeTrains.RemoveAt(i);
    }

    private void OnValidate()
    {
        firstTrainDelay = Mathf.Max(0f, firstTrainDelay);
        repeatInterval = Mathf.Max(1f, repeatInterval);
        travelSpeed = Mathf.Max(0.1f, travelSpeed);
        acceleration = Mathf.Max(0.01f, acceleration);
        wagonCount = Mathf.Clamp(wagonCount, 1, 20);
        trainScale = Mathf.Max(0.01f, trainScale);
        engineYawCorrection = Mathf.Clamp(engineYawCorrection, -180f, 180f);
        wagonYawCorrection = Mathf.Clamp(wagonYawCorrection, -180f, 180f);
        lastWagonYawCorrection = Mathf.Clamp(lastWagonYawCorrection, -180f, 180f);
        couplingGap = Mathf.Max(0f, couplingGap);
        railClearance = Mathf.Max(0f, railClearance);
    }

    private void OnDrawGizmosSelected()
    {
        if (routeStart == null || routeEnd == null) return;
        Gizmos.color = new Color(0.95f, 0.62f, 0.12f, 1f);
        Gizmos.DrawLine(routeStart.position, routeEnd.position);
        Gizmos.DrawWireSphere(routeStart.position, 2f);
        Gizmos.color = new Color(0.75f, 0.18f, 0.12f, 1f);
        Gizmos.DrawWireSphere(routeEnd.position, 2f);
    }

    private sealed class ActiveTrain
    {
        public GameObject Root;
        public Rigidbody Body;
        public Vector3 Start;
        public Vector3 End;
        public Vector3 Direction;
        public float RouteLength;
        public float ConsistLength;
        public float Distance;
        public float Speed;
    }
}
