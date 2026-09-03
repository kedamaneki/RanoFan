using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// プレイヤー死亡時の「YOU DIED」表示と自動リトライ（リスポーン）を担当します。
/// PlayerRobot などプレイヤー本体にアタッチしてください。
/// </summary>
public class PlayerDeathHandler : MonoBehaviour
{
    [Header("参照")]
    [SerializeField] private CombatStats combatStats;
    [SerializeField] private Text youDiedText;

    [Header("リスポーン")]
    [Tooltip("復活までの待ち時間（秒）")]
    [SerializeField] private float respawnDelay = 2f;

    [Tooltip("未設定ならゲーム開始時の位置へ復活")]
    [SerializeField] private Vector3 respawnPosition;

    [Tooltip("開始位置を自動記録するか")]
    [SerializeField] private bool useStartPositionAsRespawn = true;

    private Transform _movementTransform;
    private CharacterController _characterController;
    private Vector3 _initialWorldPosition;
    private Quaternion _initialWorldRotation;
    private bool _isDead;
    private Coroutine _deathRoutine;

    private void Awake()
    {
        if (combatStats == null)
        {
            combatStats = GetComponent<CombatStats>();
        }

        _characterController = GetComponentInChildren<CharacterController>();
        _movementTransform = _characterController != null ? _characterController.transform : transform;
    }

    private void Start()
    {
        _initialWorldPosition = _movementTransform.position;
        _initialWorldRotation = _movementTransform.rotation;

        if (combatStats == null)
        {
            Debug.LogWarning("PlayerDeathHandler: CombatStats が見つかりません。");
            return;
        }

        combatStats.OnDied += HandleDeath;

        if (youDiedText != null)
        {
            youDiedText.gameObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (combatStats != null)
        {
            combatStats.OnDied -= HandleDeath;
        }
    }

    private void HandleDeath()
    {
        if (_isDead)
        {
            return;
        }

        _deathRoutine = StartCoroutine(DeathAndRespawnRoutine());
    }

    private IEnumerator DeathAndRespawnRoutine()
    {
        _isDead = true;
        SetPlayerControlEnabled(false);

        if (youDiedText != null)
        {
            youDiedText.gameObject.SetActive(true);
        }

        yield return new WaitForSeconds(respawnDelay);

        if (youDiedText != null)
        {
            youDiedText.gameObject.SetActive(false);
        }

        Vector3 targetPosition = useStartPositionAsRespawn ? _initialWorldPosition : respawnPosition;
        Quaternion targetRotation = useStartPositionAsRespawn ? _initialWorldRotation : _movementTransform.rotation;

        if (_characterController != null)
        {
            _characterController.enabled = false;
        }

        // 実際に動く Robot 子オブジェクトを開始位置へ戻す（親だけ動かしても見た目は動かない）
        _movementTransform.SetPositionAndRotation(targetPosition, targetRotation);

        if (_movementTransform != transform)
        {
            transform.position = targetPosition;
            transform.rotation = targetRotation;
        }

        if (_characterController != null)
        {
            _characterController.enabled = true;
        }

        combatStats.FullHeal();
        SetPlayerControlEnabled(true);

        _isDead = false;
        _deathRoutine = null;
    }

    private void SetPlayerControlEnabled(bool enabled)
    {
        PlayerController playerController = GetComponent<PlayerController>();
        if (playerController != null)
        {
            playerController.enabled = enabled;
        }

        PlayerAttackController attackController = GetComponent<PlayerAttackController>();
        if (attackController != null)
        {
            attackController.enabled = enabled;
        }

        StarterAssets.ThirdPersonController thirdPerson = GetComponentInChildren<StarterAssets.ThirdPersonController>();
        if (thirdPerson != null)
        {
            thirdPerson.enabled = enabled;
        }
    }
}
