using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum TurretEnemyState
{
    Sleep,
    Default,
    Hitstun
}

public class TurretEnemyController : MonoBehaviour
{
    [Header("Movement")]
    [Tooltip("rate at which enemy reaches max speed")]
    public float MovementSharpness = 10f;
    [Tooltip("percentage of knockback that is ignored for turret enemy type")]
    public float KnockbackReductionFactor = 0.75f;

    [Header("Attacking")]
    public GameObject ProjectilePrefab;
    [Tooltip("layers for checking player line of sight raycast")]
    public LayerMask RaycastLayermask;
    public float RaycastRadius = 0.5f;
    public float RaycastRange = 3f;
    public float AttackCooldown = 5f;
    public float SpreadAngle = 3f;

    [Header("Misc.")]
    public float MaxHP = 5f;
    public float WakeupRadius = 10f;

    // public and hidden
    [HideInInspector] public Rigidbody2D Rb;
    [HideInInspector] public Animator Anim;
    [HideInInspector] public TurretEnemyState CurrentEnemyState = TurretEnemyState.Sleep;

    // private variables
    private GameObject _player;
    private CapsuleCollider2D _playerCollider;
    private float _hp;
    private float _attackCooldownTimer = 0f;
    private Vector2 _targetVelocity = Vector2.zero;
    // Hitstun
    private float _hitStunTimer = 0f;
    // Animation States
    private bool _isHitStunned = false;
    private bool _isSleeping = true;
    private bool _isAttacking = false;

    // Start is called before the first frame update
    void Start()
    {
        Rb = GetComponent<Rigidbody2D>();
        Anim = GetComponent<Animator>();

        _player = GameObject.Find("Dimwick");
        _playerCollider = _player.GetComponent<CapsuleCollider2D>();

        _hp = MaxHP;
    }

    #region ENEMY STATES
    public void TransitionToState(TurretEnemyState newState)
    {
        TurretEnemyState tmpInitialState = CurrentEnemyState;
        OnStateExit(tmpInitialState, newState);
        CurrentEnemyState = newState;
        OnStateEnter(newState, tmpInitialState);
    }

    public void OnStateEnter(TurretEnemyState state, TurretEnemyState fromState)
    {
        switch (state)
        {
            case TurretEnemyState.Sleep:
                _isSleeping = true;
                break;
            case TurretEnemyState.Default:
                break;
            case TurretEnemyState.Hitstun:
                _isHitStunned = true;
                break;
        }
    }

    public void OnStateExit(TurretEnemyState state, TurretEnemyState toState)
    {
        switch (state)
        {
            case TurretEnemyState.Sleep:
                _isSleeping = false;
                break;
            case TurretEnemyState.Default:
                break;
            case TurretEnemyState.Hitstun:
                _isHitStunned = false;
                break;
        }
    }
    #endregion

    // Update is called once per frame
    void Update()
    {
        switch(CurrentEnemyState)
        {
            case TurretEnemyState.Sleep:

                // wake up if player within range
                if (Vector2.Distance(_player.transform.position, transform.position) < WakeupRadius)
                    TransitionToState(TurretEnemyState.Default);

                break;
            case TurretEnemyState.Default:

                #region MOVEMENT
                // turret enemy does not move freely on its own
                _targetVelocity = Vector2.zero;

                // update velocity based on target and current velocities
                Rb.velocity = Vector2.Lerp(Rb.velocity, _targetVelocity, 1 - Mathf.Exp(-MovementSharpness * Time.deltaTime));
                #endregion

                #region ATTACKING
                if (_attackCooldownTimer > 0f)
                {
                    _attackCooldownTimer -= Time.deltaTime;
                }
                else
                {
                    // check for line of sight to player
                    RaycastHit2D hit = Physics2D.CircleCast(transform.position, RaycastRadius,
                        (Vector2)_player.transform.position + _playerCollider.offset - Vector2.up * _playerCollider.size.y - (Vector2)transform.position,
                        RaycastRange, RaycastLayermask);

                    // shows when enemy is attempting to lock on to player
                    Debug.DrawRay(transform.position,
                        ((Vector2)_player.transform.position + _playerCollider.offset - Vector2.up * _playerCollider.size.y - (Vector2)transform.position).normalized * RaycastRange,
                        Color.green);

                    if (hit.collider != null && hit.collider.CompareTag("Player"))
                    {
                        Instantiate(ProjectilePrefab, transform.position, Quaternion.Euler(0, 0,
                            Vector2.SignedAngle(Vector2.right, (Vector2)_player.transform.position - (Vector2)transform.position)
                            + Random.Range(-SpreadAngle, SpreadAngle)));

                        _attackCooldownTimer = AttackCooldown; // put attack on cooldown
                        _isAttacking = true; // used for animation state
                    }
                }
                #endregion

                break;
            case TurretEnemyState.Hitstun:

                #region HITSTUN HANDLING
                if (_hitStunTimer < 0f) // ends hitstun
                    TransitionToState(TurretEnemyState.Default);
                else
                {
                    // no controls for movement or attacking

                    // update velocity based on target and current velocities
                    Rb.velocity = Vector2.Lerp(Rb.velocity, _targetVelocity, 1 - Mathf.Exp(-MovementSharpness * Time.deltaTime));

                    _hitStunTimer -= Time.deltaTime;
                }
                #endregion

                break;
        }

        #region ANIMATION
        Anim.SetBool("isSleeping", _isSleeping);
        Anim.SetBool("isHitstun", _isHitStunned);
        if (_isAttacking)
        {
            Anim.SetTrigger("attack");
            _isAttacking = false;
        }
        else
        {
            Anim.ResetTrigger("attack");
        }
        #endregion

        // destroy object when dies (Replace with death animation??)
        if (_hp < 0f) Destroy(gameObject);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("PlayerBullet")) // apply damage and hitstun
        {
            collision.gameObject.TryGetComponent<Projectile>(out Projectile projectile);
            if (projectile != null)
            {
                TransitionToState(TurretEnemyState.Hitstun);

                // decrement hp
                _hp -= projectile.Damage;
                // start hitstun timer
                _hitStunTimer = projectile.HitstunTime;

                // set knockback based on bullet rotation and knockback stats
                Vector2 knockback = Quaternion.Euler(0, 0, collision.transform.rotation.eulerAngles.z) 
                    * Vector2.right * projectile.KnockbackSpeed * (1 - KnockbackReductionFactor);
                _targetVelocity += knockback;
            }
            else
                Debug.LogError("Invalid player projectile collison");
        }
    }
}
