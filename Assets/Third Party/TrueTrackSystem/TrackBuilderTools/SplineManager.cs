using UnityEngine;
using UnityEngine.Splines; // Ensure you are using the correct namespace
using System.Collections.Generic;


namespace TrueTrackSystem
{
    public class SplineManager : MonoBehaviour
    {
        public GameObject[] splinePrefabs;
        public float tangentValue;
        public float closeThreshold = 0.1f; // Adjust this value as needed to determine "closeness"

        // Make the method public so it can be called from the editor script
        public void CreateConnectedSpline()
        {
            if (splinePrefabs == null || splinePrefabs.Length < 2)
            {
                Debug.LogError("Not enough spline prefabs to join.");
                return;
            }

            // Create a new spline to hold the combined splines
            Spline combinedSpline = new Spline();

            for (int i = 0; i < splinePrefabs.Length; i++)
            {
                SplineContainer splineContainer = splinePrefabs[i].GetComponent<SplineContainer>();

                if (splineContainer == null)
                {
                    Debug.LogError("SplineContainer component missing from prefab.");
                    continue;
                }

                Spline currentSpline = splineContainer.Spline;
                Transform splineTransform = splinePrefabs[i].transform;

                // Ensure the spline has at least 2 knots
                if (currentSpline.Count < 2)
                {
                    Debug.LogError("Each spline must have at least 2 knots.");
                    continue;
                }

                // Add knots from the current spline to the combined spline
                for (int j = 0; j < currentSpline.Count; j++)
                {
                    var knot = currentSpline[j];
                    if (i > 0 && j == 0) // Skip the first knot if it's not the first spline
                    {
                        continue;
                    }

                    // Convert local positions to world positions
                    Vector3 worldPosition = splineTransform.TransformPoint(knot.Position);
                    Vector3 worldTangentIn = splineTransform.TransformDirection(knot.TangentIn) + worldPosition;
                    Vector3 worldTangentOut = splineTransform.TransformDirection(knot.TangentOut) + worldPosition;

                    // Adjust the Z component of tangents to meet the required values
                    Vector3 adjustedTangentIn = Vector3.Lerp(worldPosition, worldTangentIn, 0.75f);
                    Vector3 adjustedTangentOut = Vector3.Lerp(worldPosition, worldTangentOut, 0.75f);

                    // Ensure the X and Y axes of the adjusted tangents remain at 0
                    adjustedTangentIn.x = worldPosition.x;
                    adjustedTangentIn.y = worldPosition.y;
                    adjustedTangentOut.x = worldPosition.x;
                    adjustedTangentOut.y = worldPosition.y;
                    adjustedTangentIn.z = worldPosition.z - tangentValue;
                    adjustedTangentOut.z = worldPosition.z + tangentValue;

                    Quaternion worldRotation = splineTransform.rotation * knot.Rotation;

                    // Create a new BezierKnot with the adjusted tangents
                    BezierKnot newKnot = new BezierKnot(worldPosition, adjustedTangentIn - worldPosition, adjustedTangentOut - worldPosition, worldRotation);

                    // Add the new knot to the combined spline
                    combinedSpline.Add(newKnot);
                }
            }

            // Check if the first and last knots are close to each other
            if (combinedSpline.Count > 1)
            {
                Vector3 firstKnotPosition = combinedSpline[0].Position;
                Vector3 lastKnotPosition = combinedSpline[combinedSpline.Count - 1].Position;
                float distance = Vector3.Distance(firstKnotPosition, lastKnotPosition);

                if (distance < closeThreshold)
                {
                    // Remove the last knot
                    combinedSpline.RemoveAt(combinedSpline.Count - 1);
                    // Set the spline as closed
                    combinedSpline.Closed = true;
                    Debug.Log("Spline is closed and the last knot is removed.");
                }
            }

            // Create a new GameObject to hold the combined spline
            GameObject combinedSplineObject = new GameObject("CombinedSpline");
            SplineContainer combinedSplineContainer = combinedSplineObject.AddComponent<SplineContainer>();

            // Assign the combined spline to the new SplineContainer
            combinedSplineContainer.Spline = combinedSpline;

            // Debugging: Log the completion of the spline creation
            Debug.Log("Combined spline created with total knots: " + combinedSpline.Count);
        }
    }
}