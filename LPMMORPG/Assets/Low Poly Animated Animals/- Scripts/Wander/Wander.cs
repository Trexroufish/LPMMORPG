using UnityEngine;
using System.Collections;
#if UNITY_EDITOR
using UnityEditor;
#endif

[RequireComponent(typeof(Animator)), RequireComponent(typeof(CharacterController))]
public class Wander : MonoBehaviour
{
    [SerializeField]
    private float walkSpeed = 3;
    public float WalkSpeed
    {
        get
        {
            return walkSpeed;
        }
        set
        {
            Undo.RecordObject(this, "Change Walk Speed");
            walkSpeed = value;
        }
    }

    [SerializeField]
    private float turnSpeed = 2;
    public float TurnSpeed
    {
        get
        {
            return turnSpeed;
        }
        set
        {
            Undo.RecordObject(this, "Change Turn Speed");
            turnSpeed = value;
        }
    }

    [SerializeField]
    private float minIdleTime = 0.5f;
    public float MinIdleTime
    {
        get
        {
            return minIdleTime;
        }
        set
        {
            Undo.RecordObject(this, "Change Min Idle Time");
            minIdleTime = Mathf.Clamp(value, 0, maxIdleTime);
        }
    }

    [SerializeField]
    private float maxIdleTime = 2.5f;
    public float MaxIdleTime
    {
        get
        {
            return maxIdleTime;
        }
        set
        {

            Undo.RecordObject(this, "Change Max Idle Time");
            maxIdleTime = Mathf.Clamp(value, minIdleTime, 20);
        }
    }

    [SerializeField]
    private float wanderRange = 10f;
    public float WanderRange
    {
        get
        {
            return wanderRange;
        }
        set
        {
#if UNITY_EDITOR
            SceneView.RepaintAll();
#endif
            Undo.RecordObject(this, "Change Wander Range");
            wanderRange = value;
        }
    }

    [HideInInspector]
    public bool showGizmos = false;

    private Vector3 originalPos;
    private Animator animator;
    private CharacterController characterController;
    private Color rangeColor = new Color(0.502f, 0f, 0f, 0.7f);
    private Vector3 currentTarget;
    private float currentTurnSpeed;

    public void OnDrawGizmos()
    {
        if (!showGizmos)
            return;

        Gizmos.color = rangeColor;
        Gizmos.DrawWireSphere(originalPos == Vector3.zero ? transform.position : originalPos, wanderRange);

        if (!Application.isPlaying)
            return;

        if (currentTarget != Vector3.zero)
        {
            Gizmos.DrawSphere(currentTarget + new Vector3(0f, 0.1f, 0f), 0.2f);
            Gizmos.DrawLine(transform.position, currentTarget);
        }
    }

    private void Awake()
    {
        originalPos = transform.position;
        animator = GetComponent<Animator>();
        characterController = GetComponent<CharacterController>();
    }


    private void Start()
    {
        StartCoroutine(Idle(Random.Range(0, (MaxIdleTime/2) * 100)/100));
    }

    private IEnumerator Walk(Vector3 target)
    {
        currentTarget = target;
        currentTurnSpeed = turnSpeed;
        animator.SetBool("isWalking", true);

        while (Vector3.Distance(transform.position, target) > 0.25f)
        {
            characterController.SimpleMove(transform.TransformDirection(Vector3.forward) * walkSpeed);

            Vector3 relativePos = target - transform.position;
            Quaternion rotation = Quaternion.LookRotation(relativePos);
            transform.rotation = Quaternion.Slerp(transform.rotation, rotation, Time.deltaTime * currentTurnSpeed);
            currentTurnSpeed += Time.deltaTime;

            yield return null;
        }

        currentTarget = Vector3.zero;
        StartCoroutine(Idle(GetIdleTime()));
    }

    private IEnumerator Idle(float idleTime)
    {
        animator.SetBool("isWalking", false);

        yield return new WaitForSeconds(idleTime);

        StartCoroutine(Walk(RandonPointInRange()));
    }

    private Vector3 RandonPointInRange()
    {
        Vector3 randomPoint = originalPos + Random.insideUnitSphere * wanderRange;
        return new Vector3(randomPoint.x, transform.position.y, randomPoint.z);
    }

    private float GetIdleTime()
    {
        return Random.Range(minIdleTime * 100, maxIdleTime * 100) / 100;
    }
}