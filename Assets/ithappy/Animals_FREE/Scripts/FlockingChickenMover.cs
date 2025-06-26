using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Controller;

[RequireComponent(typeof(CreatureMover))]
[RequireComponent(typeof(NavMeshAgent))]
public class FlockingChickenMover : MonoBehaviour
{
    public enum ChickenState
    {
        Idle,
        Flocking,
        Following
    }

    [Header("State Machine Settings")]
    [SerializeField] private float playerDistanceForFollowing = 10f;
    [SerializeField] private int neighborCountForFlocking = 2;
    [SerializeField] private float idleTime = 3f;
    [SerializeField] private float stateCheckInterval = 0.5f;

    [Header("Flocking Settings")]
    [SerializeField] private float neighborRadius = 5f;
    [SerializeField] private float separationRadius = 1.5f;
    [SerializeField] private float obstacleAvoidanceRadius = 2f;

    [Header("Behavior Weights")]
    [SerializeField] private float cohesionWeight = 1f;
    [SerializeField] private float alignmentWeight = 1f;
    [SerializeField] private float separationWeight = 2f;
    [SerializeField] private float avoidanceWeight = 3f;

    [Header("Environment Settings")]
    [SerializeField] private LayerMask obstacleMask;

    private CreatureMover movement;
    private NavMeshAgent agent;
    private static readonly List<FlockingChickenMover> allChickens = new();
    private Transform player;

    private Vector2 inputDir;
    private Vector3 target;
    private bool isRun = true;
    private bool isMoving = true;

    private float repathTimer = 0f;
    private const float repathInterval = 0.25f;

    // State Machine Variables
    private ChickenState currentState = ChickenState.Idle;
    private float stateTimer = 0f;
    private float stateCheckTimer = 0f;
    private Vector3 lastPosition;
    private bool hasReachedTarget = false;

    private void Awake()
    {
        movement = GetComponent<CreatureMover>();
        agent = GetComponent<NavMeshAgent>();

        // Disable NavMeshAgent control — we're using CreatureMover
        agent.updatePosition = false;
        agent.updateRotation = false;

        allChickens.Add(this);
    }

    private void OnDestroy()
    {
        allChickens.Remove(this);
    }

    private void Start()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            player = playerObj.transform;
        }
        else
        {
            Debug.LogWarning("FlockingChickenMover: No GameObject with tag 'Player' found in the scene.");
            player = null;
        }
        lastPosition = transform.position;
        ChangeState(ChickenState.Idle);
    }

    private void Update()
    {
        if (player == null) return;

        stateTimer += Time.deltaTime;
        stateCheckTimer += Time.deltaTime;

        // Check for state transitions periodically
        if (stateCheckTimer >= stateCheckInterval)
        {
            stateCheckTimer = 0f;
            CheckStateTransitions();
        }

        // Execute current state behavior
        switch (currentState)
        {
            case ChickenState.Idle:
                UpdateIdleState();
                break;
            case ChickenState.Flocking:
                UpdateFlockingState();
                break;
            case ChickenState.Following:
                UpdateFollowingState();
                break;
        }

        lastPosition = transform.position;
    }

    private void CheckStateTransitions()
    {
        float distanceToPlayer = Vector3.Distance(transform.position, player.position);
        int nearbyChickens = GetNearbyChickenCount();

        switch (currentState)
        {
            case ChickenState.Idle:
                if (distanceToPlayer > playerDistanceForFollowing)
                {
                    ChangeState(ChickenState.Following);
                }
                else if (nearbyChickens >= neighborCountForFlocking)
                {
                    ChangeState(ChickenState.Flocking);
                }
                else if (stateTimer >= idleTime)
                {
                    // Random chance to start moving
                    if (Random.value < 0.3f)
                    {
                        ChangeState(nearbyChickens > 0 ? ChickenState.Flocking : ChickenState.Following);
                    }
                }
                break;

            case ChickenState.Flocking:
                if (distanceToPlayer > playerDistanceForFollowing * 1.5f)
                {
                    ChangeState(ChickenState.Following);
                }
                else if (nearbyChickens < neighborCountForFlocking && stateTimer > 2f)
                {
                    ChangeState(ChickenState.Idle);
                }
                break;

            case ChickenState.Following:
                if (distanceToPlayer <= playerDistanceForFollowing * 0.8f)
                {
                    if (nearbyChickens >= neighborCountForFlocking)
                    {
                        ChangeState(ChickenState.Flocking);
                    }
                    else
                    {
                        ChangeState(ChickenState.Idle);
                    }
                }
                break;
        }
    }

    private void ChangeState(ChickenState newState)
    {
        if (currentState == newState) return;

        // Exit current state
        ExitState(currentState);

        // Enter new state
        currentState = newState;
        stateTimer = 0f;
        EnterState(newState);
    }

    private void EnterState(ChickenState state)
    {
        switch (state)
        {
            case ChickenState.Idle:
                isMoving = false;
                isRun = false;
                break;
            case ChickenState.Flocking:
                isMoving = true;
                isRun = false;
                break;
            case ChickenState.Following:
                isMoving = true;
                isRun = true;
                break;
        }
    }

    private void ExitState(ChickenState state)
    {
        // Clean up any state-specific behavior if needed
    }

    private void UpdateIdleState()
    {
        // Minimal movement, just avoid obstacles
        Vector3 avoidance = CalculateObstacleAvoidance();
        
        if (avoidance.magnitude > 0.1f)
        {
            inputDir = new Vector2(avoidance.x, avoidance.z);
            isMoving = true;
        }
        else
        {
            inputDir = Vector2.zero;
            isMoving = false;
        }

        target = transform.position;
        movement.SetInput(inputDir, target, isRun, isMoving);
    }

    private void UpdateFlockingState()
    {
        Vector3 flockingDirection = CalculateFlockingBehavior();
        Vector3 avoidance = CalculateObstacleAvoidance();
        
        Vector3 finalDir = flockingDirection + avoidance;
        finalDir = Vector3.ProjectOnPlane(finalDir, Vector3.up).normalized;

        inputDir = new Vector2(finalDir.x, finalDir.z);
        target = transform.position + finalDir * 5f; // Move towards flocking direction

        movement.SetInput(inputDir, target, isRun, isMoving);
    }

    private void UpdateFollowingState()
    {
        repathTimer += Time.deltaTime;
        if (repathTimer >= repathInterval)
        {
            repathTimer = 0f;
            agent.SetDestination(player.position);
        }

        Vector3 navTargetDir = agent.desiredVelocity.magnitude > 0.001f ? agent.desiredVelocity.normalized : Vector3.zero;
        Vector3 avoidance = CalculateObstacleAvoidance();
        Vector3 separation = CalculateSeparation(); // Avoid crowding other chickens

        Vector3 finalDir = navTargetDir + avoidance + separation;
        finalDir = Vector3.ProjectOnPlane(finalDir, Vector3.up).normalized;

        inputDir = new Vector2(finalDir.x, finalDir.z);
        target = player.position;

        movement.SetInput(inputDir, target, isRun, isMoving);
        // Only sync agent if positions are significantly different
        if (Vector3.Distance(agent.nextPosition, transform.position) > 0.01f)
        {
            agent.nextPosition = transform.position;
        }
    }

    private Vector3 CalculateFlockingBehavior()
    {
        Vector3 cohesion = Vector3.zero;
        Vector3 separation = Vector3.zero;
        Vector3 alignment = Vector3.zero;
        int neighborCount = 0;

        foreach (var other in allChickens)
        {
            if (other == this) continue;

            float dist = Vector3.Distance(transform.position, other.transform.position);
            if (dist < neighborRadius)
            {
                cohesion += other.transform.position;
                alignment += other.transform.forward;

                if (dist < separationRadius)
                    separation += (transform.position - other.transform.position) / dist;

                neighborCount++;
            }
        }

        if (neighborCount > 0)
        {
            cohesion = ((cohesion / neighborCount) - transform.position).normalized * cohesionWeight;
            alignment = (alignment / neighborCount).normalized * alignmentWeight;
            separation = separation.normalized * separationWeight;
        }

        return cohesion + alignment + separation;
    }

    private Vector3 CalculateSeparation()
    {
        Vector3 separation = Vector3.zero;
        int neighborCount = 0;

        foreach (var other in allChickens)
        {
            if (other == this) continue;

            float dist = Vector3.Distance(transform.position, other.transform.position);
            if (dist < separationRadius)
            {
                separation += (transform.position - other.transform.position) / dist;
                neighborCount++;
            }
        }

        return neighborCount > 0 ? separation.normalized * separationWeight : Vector3.zero;
    }

    private Vector3 CalculateObstacleAvoidance()
    {
        Vector3 avoidance = Vector3.zero;
        Collider[] hits = Physics.OverlapSphere(transform.position, obstacleAvoidanceRadius, obstacleMask);
        foreach (var hit in hits)
        {
            Vector3 away = transform.position - hit.ClosestPoint(transform.position);
            avoidance += away.normalized;
        }
        return avoidance.magnitude > 0.01f ? avoidance.normalized * avoidanceWeight : Vector3.zero;
    }

    private int GetNearbyChickenCount()
    {
        int count = 0;
        foreach (var other in allChickens)
        {
            if (other == this) continue;
            if (Vector3.Distance(transform.position, other.transform.position) < neighborRadius)
                count++;
        }
        return count;
    }

    // Public method to get current state (useful for debugging)
    public ChickenState GetCurrentState()
    {
        return currentState;
    }

    // Public method to force a state change (useful for external triggers)
    public void ForceStateChange(ChickenState newState)
    {
        ChangeState(newState);
    }
}
