using CesiumForUnity;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class Simulator : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private GameObject[] vehiclePrefabs;
    [SerializeField] private GameObject[] pedestrianPrefabs;
    [SerializeField] private CesiumGeoreference georeference;   
    [SerializeField] private LayerMask groundMask = ~0;  

    [Header("Settings")]
    [SerializeField] private float interpolationDelay = 0.2f;
    [SerializeField] private float vehicleRotationSpeed = 8f;
    [SerializeField] private float pedestrianRotationSpeed = 4f;
    [SerializeField] private float initialHeight = 400f;        // So agents are spawned above ground.
    [SerializeField] private float groundOffset = 0f;
    [SerializeField] private int initialVehiclePoolSize = 30; // The pool size to have of created agent prefabs.
    [SerializeField] private int initialPedestrianBufferSize = 50;
    [SerializeField] private float removeAfterSeconds = 60.0f;  // Seconds before agent is returned to the pool.
    [SerializeField] private int simulationStepsCapacity = 20;

    private readonly Dictionary<string, AgentState> activeAgents = new(); // Keys will be the agent's simulation ids.
    private readonly Queue<GameObject> vehiclePool = new();
    private readonly Queue<GameObject> pedestrianPool = new();

    enum AgentType
    {
        Vehicle,
        Pedestrian
    }

    // For saving old simulation steps.
    class TimeStep
    {
        public float time; // Real time the step was retrieved from the simulation.
        public Vector3 position;
        public float angle;
    }

    class AgentState
    {
        public GameObject obj;
        public AgentType type;
       
        public float lastStepTime;

        public Animator animator; // For pedestrians.

        public List<TimeStep> bufferedTimeSteps = new();
    }

    private void Start()
    {
        // Create instances of the prefabs before the simulation, so not everything has to be created during simulation.
        CreatePool(AgentType.Vehicle, initialVehiclePoolSize);
        CreatePool(AgentType.Pedestrian, initialPedestrianBufferSize);
    }

    // Creates a pool for the specified agent type.
    private void CreatePool(AgentType type, int count)
    {        
        for (int i = 0; i < count; i++)
        {
            GameObject obj = CreatePooledObject(type);
            obj.SetActive(false);
            GetPool(type).Enqueue(obj);
        }
    }

    // Creates a buffer prefab for specified agent type.
    private GameObject CreatePooledObject(AgentType type)
    {
        GameObject[] prefabs = GetPrefabList(type);

        if (prefabs == null || prefabs.Length == 0)
        {
            Debug.LogError($"No prefabs assigned for {type}");
            return null;
        }

        GameObject prefab = prefabs[UnityEngine.Random.Range(0, prefabs.Length)]; // Random prefab from the specified list.
        GameObject obj = Instantiate(prefab);
        obj.SetActive(false);

        return obj;
    }

    private GameObject[] GetPrefabList(AgentType type)
    {
        if (type == AgentType.Vehicle)
            return vehiclePrefabs;
        else
            return pedestrianPrefabs;
    }

    // Returns pool of pooled prefabs for specified agent type.
    private Queue<GameObject> GetPool(AgentType type)
    {
        if (type == AgentType.Vehicle)
            return vehiclePool;
        else
            return pedestrianPool;         
    }

    // Gets an instantiated object of specified type from pool to be used for simulation.
    // If buffer is empty it creates one.
    private GameObject GetObjectFromPool(AgentType type)
    {
        Queue<GameObject> pool = GetPool(type);

        GameObject obj = null;

        // Gets the first instantiaed object from the pool.
        while (pool.Count > 0 && obj == null)
        {
            obj = pool.Dequeue();
        }

        // Creates one if pool is empty.
        if (obj == null)
        {
            obj = CreatePooledObject(type);
        }

        // Activates agent and resets its position.
        obj.SetActive(true);
        obj.transform.position = Vector3.zero;
        obj.transform.rotation = Quaternion.identity;

        return obj;
    }

    // Puts agent object back in the corresponding pool.
    private void ReturnObjectToPool(GameObject obj, AgentType type)
    {
        if (obj == null)
            return;

        obj.SetActive(false); // Removes it from the scene.
        GetPool(type).Enqueue(obj);
    }

    void Update()
    {       
        // Render time is slightly older than current time to allow time for new simulation data to come.
        // We only render the agents at renderTime, so we can have frames that arrived after renderTime that have not been rendered yet.
        float renderTime = Time.time - interpolationDelay;

        // Gets the state for each active agent.
        foreach (var state in activeAgents.Values)
        {
            var bufferedTimeSteps = state.bufferedTimeSteps;

            // Skips agents with no simulation data.
            if (bufferedTimeSteps.Count == 0)
                continue;

            // Removes old simulation data (before renderTime) if the agent has enough simulation data.
            // Keeps at least 1 simulation step if the newest of the two steps is over the render time. (then agent stays still at the only step avaiable).
            // Keeps at least 2 simulation steps if both steps are newer render time (render time has not reached them) and agent stays still at oldest step, until the oldest step reachs render time (so lerp != 0).
            // Keeps at least 2 simulations steps if the oldest of the two steps is over the render time, but the newest is not. This means interpolation and agent is moving.
            while (bufferedTimeSteps.Count >= 2 && bufferedTimeSteps[1].time <= renderTime)
            {
                bufferedTimeSteps.RemoveAt(0);
            }

            if (bufferedTimeSteps.Count >= 2)
            {
                TimeStep a = bufferedTimeSteps[0];
                TimeStep b = bufferedTimeSteps[1];


                float t = Mathf.InverseLerp(a.time, b.time, renderTime);

                // Finds interpolated position between the positions of the 2 simulation steps.
                Vector3 interpolatedPosition = Vector3.Lerp(a.position, b.position, t);

                // Translates agent at interpolated position.
                Transform transform = state.obj.transform;
                transform.position = interpolatedPosition;
                //Debug.DrawLine(pos, pos + Vector3.up * 2f, Color.blue, 1f);

                //// Rotation.
                //float interpolatedAngle = Mathf.LerpAngle(a.angle, b.angle, t);

                //Quaternion targetRotation = Quaternion.Euler(0f, interpolatedAngle, 0f);

                //// Vehicles and pedestrians have different rotation speeds.
                //float rotationSpeed = state.type == AgentType.Vehicle ? vehicleRotationSpeed : pedestrianRotationSpeed;

                //transform.rotation = targetRotation;//Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * rotationSpeed);

                // Rotation based on the direction between the two timesteps.
                Vector3 direction = b.position - a.position;
                direction.y = 0f;

                // Avoid jitter when the agent barely moves.
                if (direction.sqrMagnitude > 0.0001f)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);

                    float rotationSpeed = state.type == AgentType.Vehicle
                        ? vehicleRotationSpeed
                        : pedestrianRotationSpeed;

                    transform.rotation = Quaternion.Slerp(
                        transform.rotation,
                        targetRotation,
                        Time.deltaTime * rotationSpeed
                    );
                }
            }
            else
            {              
                // This makes it so it does not render old steps, but rather places the agent at the most recent step (if agent has only 1 step the latest step is at index 0).
                TimeStep latestStep = bufferedTimeSteps[0];
                state.obj.transform.position = latestStep.position;
            }
        }

        // Counts active agents.
        /////////////////////////////////////////////////////////////////////////////////
        int vehicleCount = 0;
        int pedestrianCount = 0;

        foreach (var e in activeAgents.Values)
        {
            if (e.type == AgentType.Vehicle)
                vehicleCount++;
            else if (e.type == AgentType.Pedestrian)
                pedestrianCount++;
        }

        Debug.Log($"Vehicles: {vehicleCount}, Pedestrians: {pedestrianCount}");
        /////////////////////////////////////////////////////////////////////////////////
    }

    // Updates vehicle objects, based on the vehicle data of the current simulation step.
    public void UpdateVehicles(Vehicle[] vehicleData)
    {
        float timeNow = Time.time;

        // So we can keep track of the active agents that we need to update.
        HashSet<string> activeIds = new HashSet<string>();

        // Vehicles in the simulation step are now active agents.
        foreach (Vehicle vehicle in vehicleData)
        {
            activeIds.Add(vehicle.id);
            AgentState agentState;
            
            if (activeAgents.ContainsKey(vehicle.id))
            {
                agentState = activeAgents[vehicle.id];
            }

            // If this was not active agent, a game object needs be assigned for it.
            else
            {
                GameObject obj = GetObjectFromPool(AgentType.Vehicle);

                if (obj == null)
                    continue;

                // New state for the new agent.
                agentState = new AgentState
                {
                    obj = obj,
                    type = AgentType.Vehicle,
                    lastStepTime = timeNow,
                    bufferedTimeSteps = new List<TimeStep>()
                };

                // Keys of active agents are their simulation id.
                activeAgents[vehicle.id] = agentState;
            }
            
            agentState.lastStepTime = timeNow;
            agentState.obj.SetActive(true);

            Vector3 unityPos = LonLatToUnity(vehicle.lon, vehicle.lat, initialHeight);
        
            bool grounded = GroundAgent(ref unityPos);

            // If at new step the agent cannot be grounded, the put it at the last valid position.
            if (!grounded)
            {
                if (agentState.bufferedTimeSteps.Count > 0)
                {
                    unityPos = agentState.bufferedTimeSteps[agentState.bufferedTimeSteps.Count - 1].position;
                }
                else
                    continue; // So invalid simulation steps are not added to the buffer.
            }

            AddTimeStep(agentState, unityPos, vehicle.angle, timeNow);
        }

        // Removes or disables agents that were not in the latest simulation step.
        RemoveMissingAgents(activeIds, AgentType.Vehicle);
    }

    // Updates pedestrian objects based on pedestrian data from latest simulation step. Similar to UpdateVehicles().
    public void UpdatePedestrians(Pedestrian[] pedestrianData)
    {
        float timeNow = Time.time;

        HashSet<string> activeIds = new HashSet<string>();

        foreach (Pedestrian pedestrian in pedestrianData)
        {
            activeIds.Add(pedestrian.id);

            if (!activeAgents.TryGetValue(pedestrian.id, out var agentState))
            {
                GameObject obj = GetObjectFromPool(AgentType.Pedestrian);

                if (obj == null)
                    continue;

                agentState = new AgentState
                {
                    obj = obj,
                    type = AgentType.Pedestrian,
                    animator = obj.GetComponent<Animator>(),               
                    lastStepTime = timeNow,
                    bufferedTimeSteps = new List<TimeStep>()
                };

                activeAgents[pedestrian.id] = agentState;
            }
         
            agentState.lastStepTime = timeNow;
            agentState.obj.SetActive(true);    

            agentState.animator.SetFloat("Speed", pedestrian.speed);

            Vector3 unityPos = LonLatToUnity(pedestrian.lon, pedestrian.lat, initialHeight);

            bool grounded = GroundAgent(ref unityPos);

            // If at new step the agent cannot be grounded, the put it at the last valid position.
            if (!grounded)
            {
                if (agentState.bufferedTimeSteps.Count > 0)
                {
                    unityPos = agentState.bufferedTimeSteps[agentState.bufferedTimeSteps.Count - 1].position;
                }
                else
                    continue; // So invalid simulation steps are not added to the buffer.
            }
            
            AddTimeStep(agentState, unityPos, pedestrian.angle, timeNow);
        }

        RemoveMissingAgents(activeIds, AgentType.Pedestrian);
    }

    // Translates GPS coordinates to Unity world position using the georeference object.
    private Vector3 LonLatToUnity(double lon, double lat, double height)
    {
        // Vector the georeference object needs to translate coordinates.
        double3 earthCenteredEarthFixed =
            CesiumWgs84Ellipsoid.LongitudeLatitudeHeightToEarthCenteredEarthFixed(
                new double3(lon, lat, height)
            );

        double3 unityPosition = georeference.TransformEarthCenteredEarthFixedPositionToUnity(earthCenteredEarthFixed);
        Vector3 unityPositionVector = new Vector3((float)unityPosition.x, (float)unityPosition.y, (float)unityPosition.z);

        return unityPositionVector;
    }

    // Removes from buffer or disables agents of specified type that were not included in the last simulation step.
    private void RemoveMissingAgents(HashSet<string> activeIds, AgentType type)
    {
        // List of agents to remove from active set (agents that have not been active for some time).
        List<string> toRemove = new List<string>();
        float timeNow = Time.time;

        foreach (var activeAgent in activeAgents)
        {
            if (activeAgent.Value.type != type)
                continue;

            AgentState state = activeAgent.Value;

            // Gets only agents that are not active in the last simulation step.
            if (!activeIds.Contains(activeAgent.Key))
            {
                // Remove from scene.
                state.obj.SetActive(false);

                // If agent has not been active for specified time, also mark it for deletion from active agent set.
                if (timeNow - state.lastStepTime >= removeAfterSeconds)
                {
                    //state.bufferedTimeSteps.Clear(); // Clear buffered steps, since these are old. Not needed though since state is saved in dictionary not object.
                    ReturnObjectToPool(state.obj, state.type); // Return the obj to the pool.
                    toRemove.Add(activeAgent.Key);
                }
            }
        }

        // Removes agents that have not been active for some time.
        foreach (var id in toRemove)
        {
            activeAgents.Remove(id);
        }
    }

    // Addds valid simulation steps to the buffer of the agent.
    private void AddTimeStep(AgentState agentState, Vector3 unityPos, float angle, float time)
    {
        agentState.bufferedTimeSteps.Add(new TimeStep
        {
            time = time,
            position = unityPos,
            angle = angle
        });

        // Limits the capacity of agent's simulation steps.
        if (agentState.bufferedTimeSteps.Count > simulationStepsCapacity)
            agentState.bufferedTimeSteps.RemoveAt(0); // index 0 has the oldest step.
    }

    // Grounds agent to the ground using raycast.
    private bool GroundAgent(ref Vector3 unityPos)
    {
        Vector3 rayStart = unityPos;

        float rayDistance = 2f * initialHeight;

        if (Physics.Raycast(
            rayStart,
            Vector3.down,
            out RaycastHit hit,
            rayDistance,
            groundMask))
           // QueryTriggerInteraction))
        {
            unityPos = hit.point + Vector3.up * groundOffset;
            return true;
        }

        return false;
    }
}