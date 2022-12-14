using UnityEngine;

namespace UnityStandardAssets.Characters.ThirdPerson
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    [RequireComponent(typeof(Animator))]
    public class ThirdPersonCharacter : MonoBehaviour
    {
        public event System.Action<ThirdPersonCharacter> OnFootStep;
        public event System.Action<ThirdPersonCharacter> OnJump;
        public event System.Action<ThirdPersonCharacter> OnLand;

        [SerializeField]
        float m_MovingTurnSpeed = 360;
        [SerializeField]
        float m_StationaryTurnSpeed = 180;
        [SerializeField]
        float m_JumpPower = 12f;
        [SerializeField]
        [Range(0f, 1f)]
        float m_ghostJumpGracePeriod = 0.2f;
        [Range(1f, 4f)]
        [SerializeField]
        float m_GravityMultiplier = 2f;
        [SerializeField]
        float m_RunCycleLegOffset = 0.2f; //specific to the character in sample assets, will need to be modified to work with others
        [SerializeField]
        float m_MoveSpeedMultiplier = 1f;
        [SerializeField]
        float m_AnimSpeedMultiplier = 1f;
        [SerializeField]
        float m_GroundCheckDistance = 0.1f;
        [SerializeField]
        float m_AerialControlInfluence = 0f;
        [SerializeField]
        LayerMask crouchLayerMask = ~0;
        [SerializeField]
        LayerMask groundLayerMask = ~0;

        [Header("Particles")]
        [SerializeField]
        ParticleSystem jumpParticles = null;
        [SerializeField]
        ParticleSystem landParticles = null;
        //[Header("Walk Particles")]
        //[SerializeField]
        //ParticleSystem m_walkingParticles = null;
        //[SerializeField]
        //float m_MoveThreshold = 0.1f;

        Rigidbody m_Rigidbody;
        Animator m_Animator;
        bool m_IsGrounded;
        float m_OrigGroundCheckDistance;
        const float k_Half = 0.5f;
        float m_TurnAmount;
        float m_ForwardAmount;
        float m_lastGrounded = -1f;
        Vector3 m_GroundNormal;
        //Vector3 m_MoveDirection;
        float m_CapsuleHeight;
        Vector3 m_CapsuleCenter;
        CapsuleCollider m_Capsule;
        bool m_Crouching;
        bool m_OnFirstStep = true;

        public float ForwardAmount
        {
            get
            {
                return m_ForwardAmount;
            }
        }

        public bool IsGrounded
        {
            get
            {
                return m_IsGrounded;
            }
            private set
            {
                if(m_IsGrounded != value)
                {
                    m_IsGrounded = value;

                    // Setup time from last grounded
                    if (m_IsGrounded == true)
                    {
                        if (landParticles != null)
                        {
                            landParticles.Stop();
                            landParticles.Play();
                        }

                        m_lastGrounded = -1f;
                    }
                    else
                    {
                        m_lastGrounded = Time.time;
                    }
                }
            }
        }

        public bool CanJump
        {
            get
            {
                bool returnFlag = IsGrounded;
                if((IsGrounded == false) && (m_lastGrounded > 0) && ((Time.time - m_lastGrounded) < m_ghostJumpGracePeriod))
                {
                    returnFlag = true;
                }
                return returnFlag;
            }
        }

        void Start()
        {
            m_Animator = GetComponent<Animator>();
            m_Rigidbody = GetComponent<Rigidbody>();
            m_Capsule = GetComponent<CapsuleCollider>();
            m_CapsuleHeight = m_Capsule.height;
            m_CapsuleCenter = m_Capsule.center;

            m_IsGrounded = false;
            m_lastGrounded = -1f;

            m_Rigidbody.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezeRotationZ;
            m_OrigGroundCheckDistance = m_GroundCheckDistance;
        }


        public void Move(Vector3 originalMove, bool crouch, bool jump)
        {
            // convert the world relative moveInput vector into a local-relative
            // turn amount and forward amount required to head in the desired
            // direction.
            Vector3 processedMove = originalMove;
            if (processedMove.sqrMagnitude > 1f)
            {
                processedMove.Normalize();
            }
            processedMove = transform.InverseTransformDirection(processedMove);
            CheckGroundStatus();
            processedMove = Vector3.ProjectOnPlane(processedMove, m_GroundNormal);
            m_TurnAmount = Mathf.Atan2(processedMove.x, processedMove.z);
            m_ForwardAmount = processedMove.z;

            ApplyExtraTurnRotation();

            // control and velocity handling is different when grounded and airborne:
            if (CanJump == true)
            {
                HandleJump(crouch, jump);
            }
            if(IsGrounded == false)
            {
                HandleAirborneMovement();
            }

            ScaleCapsuleForCrouching(crouch);
            PreventStandingInLowHeadroom();

            // send input and other state parameters to the animator
            UpdateAnimator(processedMove);

            // Apply aerial controls
            HandleAerialMovement(ref originalMove);
            //UpdateParticles();
        }

        void HandleAerialMovement(ref Vector3 originalMove)
        {
            if ((IsGrounded == false) && (m_AerialControlInfluence > 0))
            {
                if (originalMove.sqrMagnitude > 0)
                {
                    if (originalMove.sqrMagnitude > 1f)
                    {
                        originalMove.Normalize();
                    }
                    originalMove = (originalMove * m_AerialControlInfluence) / Time.deltaTime;
                }
                originalMove.y = 0;

                // Add as force
                m_Rigidbody.AddForce(originalMove, ForceMode.Acceleration);
            }
        }

        //void UpdateParticles()
        //{
        //    if (/*(IsGrounded == true) && */(m_ForwardAmount > m_MoveThreshold))
        //    {
        //        if(m_walkingParticles.isPlaying == false)
        //        {
        //            m_walkingParticles.Play();
        //        }
        //    }
        //    else
        //    {
        //        if (m_walkingParticles.isPlaying == true)
        //        {
        //            m_walkingParticles.Stop();
        //        }
        //    }
        //}

        void ScaleCapsuleForCrouching(bool crouch)
        {
            if (IsGrounded && crouch)
            {
                if (m_Crouching) return;
                m_Capsule.height = m_Capsule.height / 2f;
                m_Capsule.center = m_Capsule.center / 2f;
                m_Crouching = true;
            }
            else
            {
                Ray crouchRay = new Ray(m_Rigidbody.position + Vector3.up * m_Capsule.radius * k_Half, Vector3.up);
                float crouchRayLength = m_CapsuleHeight - m_Capsule.radius * k_Half;
                if (Physics.SphereCast(crouchRay, m_Capsule.radius * k_Half, crouchRayLength, crouchLayerMask, QueryTriggerInteraction.Ignore))
                {
                    m_Crouching = true;
                    return;
                }
                m_Capsule.height = m_CapsuleHeight;
                m_Capsule.center = m_CapsuleCenter;
                m_Crouching = false;
            }
        }

        void PreventStandingInLowHeadroom()
        {
            // prevent standing up in crouch-only zones
            if (!m_Crouching)
            {
                Ray crouchRay = new Ray(m_Rigidbody.position + Vector3.up * m_Capsule.radius * k_Half, Vector3.up);
                float crouchRayLength = m_CapsuleHeight - m_Capsule.radius * k_Half;
                if (Physics.SphereCast(crouchRay, m_Capsule.radius * k_Half, crouchRayLength, crouchLayerMask, QueryTriggerInteraction.Ignore))
                {
                    m_Crouching = true;
                }
            }
        }


        void UpdateAnimator(Vector3 move)
        {
            // update the animator parameters
            m_Animator.SetFloat("Forward", m_ForwardAmount, 0.1f, Time.deltaTime);
            m_Animator.SetFloat("Turn", m_TurnAmount, 0.1f, Time.deltaTime);
            m_Animator.SetBool("Crouch", m_Crouching);
            m_Animator.SetBool("OnGround", IsGrounded);
            if (!IsGrounded)
            {
                m_Animator.SetFloat("Jump", m_Rigidbody.velocity.y);
            }

            // calculate which leg is behind, so as to leave that leg trailing in the jump animation
            // (This code is reliant on the specific run cycle offset in our animations,
            // and assumes one leg passes the other at the normalized clip times of 0.0 and 0.5)
            float runCycle =
                Mathf.Repeat(
                    m_Animator.GetCurrentAnimatorStateInfo(0).normalizedTime + m_RunCycleLegOffset, 1);
            float jumpLeg = (runCycle < k_Half ? 1 : -1) * m_ForwardAmount;
            if (IsGrounded)
            {
                m_Animator.SetFloat("JumpLeg", jumpLeg);
                if (OnFootStep != null)
                {
                    if ((m_OnFirstStep == true) && (runCycle < 0.5f))
                    {
                        OnFootStep(this);
                        m_OnFirstStep = false;
                    }
                    else if ((m_OnFirstStep == false) && (runCycle > 0.5f))
                    {
                        OnFootStep(this);
                        m_OnFirstStep = true;
                    }
                }
            }

            // the anim speed multiplier allows the overall speed of walking/running to be tweaked in the inspector,
            // which affects the movement speed because of the root motion.
            if (IsGrounded && move.magnitude > 0)
            {
                m_Animator.speed = m_AnimSpeedMultiplier;
            }
            else
            {
                // don't use that while airborne
                m_Animator.speed = 1;
            }
        }


        void HandleAirborneMovement()
        {
            // apply extra gravity from multiplier:
            Vector3 extraGravityForce = (Physics.gravity * m_GravityMultiplier) - Physics.gravity;
            m_Rigidbody.AddForce(extraGravityForce);

            m_GroundCheckDistance = m_Rigidbody.velocity.y < 0 ? m_OrigGroundCheckDistance : 0.01f;
        }


        void HandleJump(bool crouch, bool jump)
        {
            // check whether conditions are right to allow a jump:
            if (jump && !crouch && CanJump)
            {
                // jump!
                m_Rigidbody.velocity = new Vector3(m_Rigidbody.velocity.x, m_JumpPower, m_Rigidbody.velocity.z);
                IsGrounded = false;
                m_lastGrounded = -1f;
                m_Animator.applyRootMotion = false;
                m_GroundCheckDistance = 0.1f;

                if (jumpParticles != null)
                {
                    jumpParticles.Stop();
                    jumpParticles.Play();
                }

                if (OnJump != null)
                {
                    OnJump(this);
                }
            }
        }

        void ApplyExtraTurnRotation()
        {
            // help the character turn faster (this is in addition to root rotation in the animation)
            float turnSpeed = Mathf.Lerp(m_StationaryTurnSpeed, m_MovingTurnSpeed, m_ForwardAmount);
            transform.Rotate(0, m_TurnAmount * turnSpeed * Time.deltaTime, 0);
        }


        public void OnAnimatorMove()
        {
            // we implement this function to override the default root motion.
            // this allows us to modify the positional speed before it's applied.
            if (Time.deltaTime > 0)
            {
                if (IsGrounded)
                {
                    Vector3 v = (m_Animator.deltaPosition * m_MoveSpeedMultiplier) / Time.deltaTime;
                    //Vector3 additiveV = Vector3.zero;
                    //additiveV.z = m_Animator.GetFloat("forward");
                    //additiveV.x = m_Animator.GetFloat("right");
                    //v += m_Animator.transform.rotation * additiveV;

                    // we preserve the existing y part of the current velocity.
                    v.y = m_Rigidbody.velocity.y;
                    m_Rigidbody.velocity = v;
                }
            }
        }


        void CheckGroundStatus()
        {
            RaycastHit hitInfo;
#if UNITY_EDITOR
            // helper to visualise the ground check ray in the scene view
            Debug.DrawLine(transform.position + (Vector3.up * 0.1f), transform.position + (Vector3.up * 0.1f) + (Vector3.down * m_GroundCheckDistance));
#endif
            // 0.1f is a small offset to start the ray from inside the character
            // it is also good to note that the transform position in the sample assets is at the base of the character
            if (Physics.Raycast(transform.position + (Vector3.up * 0.1f), Vector3.down, out hitInfo, m_GroundCheckDistance, groundLayerMask))
            {
                if ((IsGrounded == false) && (OnLand != null))
                {
                    OnLand(this);
                }
                m_GroundNormal = hitInfo.normal;
                IsGrounded = true;
                m_Animator.applyRootMotion = true;
            }
            else
            {
                IsGrounded = false;
                m_GroundNormal = Vector3.up;
                m_Animator.applyRootMotion = false;
            }
        }
    }
}
