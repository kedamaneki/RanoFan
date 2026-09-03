using System;
using UnityEngine;

// =============================================================================
// 戦闘中インスタント・クラフト（即席武器の現地調達）
// 連携: InventoryManager / CombatActionFeedbackManager / PlayerRobot
// =============================================================================

/// <summary>即席クラフトの消費対象となる魔物素材・鉱石の ID 定数。</summary>
public static class InstantCraftMaterialIds
{
    /// <summary>大森林の王グリフォン由来の骨片。</summary>
    public const string GryphonBone = "GryphonBone";

    /// <summary>野外で拾える鉄鉱石。</summary>
    public const string IronOre = "IronOre";
}

/// <summary>即席武器アイテムのインベントリ ID 定数。</summary>
public static class InstantCraftWeaponItemIds
{
    /// <summary>骨素材から錬成する即席の骨槍。</summary>
    public const string BoneSpear = "instant_bone_spear";

    /// <summary>鉄鉱石から錬成する即席の鉄尖槍。</summary>
    public const string IronSpear = "instant_iron_spear";
}

/// <summary>即席武器1種分の戦闘パラメータ（銘・攻撃タイプ・耐久）。</summary>
public sealed class InstantCraftWeaponProfile
{
    /// <summary>インベントリ上のアイテム ID。</summary>
    public string ItemId { get; }

    /// <summary>銘（表示名。例: 即席の骨槍）。</summary>
    public string EngravingName { get; }

    /// <summary>戦闘ログ用攻撃タイプ（Slash / Thrust / Strike）。</summary>
    public string AttackType { get; }

    /// <summary>初期耐久値。</summary>
    public int MaxDurability { get; }

    /// <summary>1個あたりの重量（kg）。</summary>
    public float Weight { get; }

    /// <summary>武器種（WeaponRestrictionSystem 連携用）。</summary>
    public WeaponType WeaponType { get; }

    /// <summary>即席武器プロファイルを構築します。</summary>
    public InstantCraftWeaponProfile(
        string itemId,
        string engravingName,
        string attackType,
        int maxDurability,
        float weight,
        WeaponType weaponType)
    {
        ItemId = itemId ?? string.Empty;
        EngravingName = engravingName ?? string.Empty;
        AttackType = attackType ?? CombatActionFeedbackManager.PlayerAttackTypeThrust;
        MaxDurability = Mathf.Max(1, maxDurability);
        Weight = Mathf.Max(0f, weight);
        WeaponType = weaponType;
    }
}

/// <summary>消費素材 ID から即席武器プロファイルへ変換するレジストリ。</summary>
public static class InstantCraftWeaponRegistry
{
    private static readonly InstantCraftWeaponProfile BoneSpearProfile = new InstantCraftWeaponProfile(
        InstantCraftWeaponItemIds.BoneSpear,
        "即席の骨槍",
        CombatActionFeedbackManager.PlayerAttackTypeThrust,
        maxDurability: 3,
        weight: 1.8f,
        WeaponType.Spear);

    private static readonly InstantCraftWeaponProfile IronSpearProfile = new InstantCraftWeaponProfile(
        InstantCraftWeaponItemIds.IronSpear,
        "即席の鉄尖槍",
        CombatActionFeedbackManager.PlayerAttackTypeThrust,
        maxDurability: 3,
        weight: 2.4f,
        WeaponType.Spear);

    /// <summary>インスタント・クラフトで優先的に消費する素材 ID の順序。</summary>
    public static readonly string[] MaterialConsumePriority =
    {
        InstantCraftMaterialIds.GryphonBone,
        InstantCraftMaterialIds.IronOre
    };

    /// <summary>消費した素材 ID から生成する即席武器プロファイルを返します。</summary>
    /// <param name="consumedMaterialId">InventoryManager から消費された素材 ID</param>
    public static InstantCraftWeaponProfile ResolveWeaponFromMaterial(string consumedMaterialId)
    {
        if (string.Equals(consumedMaterialId, InstantCraftMaterialIds.GryphonBone, StringComparison.Ordinal))
        {
            return BoneSpearProfile;
        }

        if (string.Equals(consumedMaterialId, InstantCraftMaterialIds.IronOre, StringComparison.Ordinal))
        {
            return IronSpearProfile;
        }

        return BoneSpearProfile;
    }

    /// <summary>即席武器プロファイルから ItemData を生成します。</summary>
    public static ItemData CreateInventoryItem(InstantCraftWeaponProfile profile)
    {
        if (profile == null)
        {
            return null;
        }

        return new ItemData
        {
            id = profile.ItemId,
            itemName = $"銘：{profile.EngravingName}",
            weight = profile.Weight,
            maxStack = 1
        };
    }
}

/// <summary>
/// PlayerRobot に即席武器を物理アタッチし、耐久・攻撃タイプを戦闘へ配線します。
/// </summary>
public class PlayerInstantWeaponLoadout : MonoBehaviour
{
    private const string VisualChildName = "InstantCraftWeaponVisual";

    [Header("ビジュアル（未設定時は Primitive 槍を自動生成）")]
    [SerializeField] private Vector3 weaponLocalPosition = new Vector3(0.38f, 0.12f, 0.52f);
    [SerializeField] private Vector3 weaponLocalEuler = new Vector3(12f, -18f, 88f);
    [SerializeField] private Vector3 weaponLocalScale = new Vector3(0.07f, 0.07f, 0.95f);

    private InstantCraftWeaponProfile equippedProfile;
    private GameObject weaponVisualObject;
    private int currentDurability;

    /// <summary>即席武器が装備されているか。</summary>
    public bool HasEquippedWeapon => equippedProfile != null && currentDurability > 0;

    /// <summary>装備中の攻撃タイプ（未装備時は null）。</summary>
    public string EquippedAttackType => HasEquippedWeapon ? equippedProfile.AttackType : null;

    /// <summary>装備中アイテムのインベントリ ID。</summary>
    public string EquippedItemId => equippedProfile?.ItemId;

    /// <summary>残り耐久値。</summary>
    public int CurrentDurability => currentDurability;

    /// <summary>装備中の銘。</summary>
    public string EquippedEngravingName => equippedProfile?.EngravingName;

    /// <summary>指定 Transform から PlayerInstantWeaponLoadout を解決します。</summary>
    public static PlayerInstantWeaponLoadout Resolve(Transform playerRoot)
    {
        if (playerRoot == null)
        {
            return null;
        }

        PlayerInstantWeaponLoadout onRoot = playerRoot.GetComponent<PlayerInstantWeaponLoadout>();
        if (onRoot != null)
        {
            return onRoot;
        }

        return playerRoot.GetComponentInChildren<PlayerInstantWeaponLoadout>();
    }

    /// <summary>PlayerRobot にコンポーネントが無ければ追加して返します。</summary>
    public static PlayerInstantWeaponLoadout EnsureOn(Transform playerRoot)
    {
        if (playerRoot == null)
        {
            GameObject found = GameObject.Find("PlayerRobot");
            if (found == null)
            {
                found = GameObject.Find("PlayerRobot ");
            }

            if (found == null)
            {
                return null;
            }

            playerRoot = found.transform;
        }

        PlayerInstantWeaponLoadout existing = Resolve(playerRoot);
        if (existing != null)
        {
            return existing;
        }

        return playerRoot.gameObject.AddComponent<PlayerInstantWeaponLoadout>();
    }

    /// <summary>即席武器を強制装備し、ビジュアルを親 Transform へアタッチします。</summary>
    /// <param name="profile">生成した即席武器プロファイル</param>
    /// <param name="visualParent">PlayerRobot のビジュアル Transform（Geometry 子想定）</param>
    public void Equip(InstantCraftWeaponProfile profile, Transform visualParent)
    {
        if (profile == null || visualParent == null)
        {
            Debug.LogWarning("[PlayerInstantWeaponLoadout] Equip に null が渡されたため装備をスキップしました。");
            return;
        }

        if (HasEquippedWeapon && !string.Equals(EquippedItemId, profile.ItemId, StringComparison.Ordinal))
        {
            InventoryManager inventory = InventoryManager.Instance ?? InventoryManager.EnsureInstance();
            inventory.TryConsumeItem(EquippedItemId, 1);
        }

        equippedProfile = profile;
        currentDurability = profile.MaxDurability;
        RebuildVisual(visualParent);

        Debug.Log(
            $"<color=#80DEEA><b>[PlayerInstantWeaponLoadout]</b></color> " +
            $"銘：{profile.EngravingName} を PlayerRobot に強制装備 " +
            $"（{profile.AttackType} / 耐久 {currentDurability}/{profile.MaxDurability}）");
    }

    /// <summary>攻撃1回分の耐久を消費します。耐久0で false を返しビジュアルを外します。</summary>
    public bool ConsumeDurabilityOnAttack()
    {
        if (!HasEquippedWeapon)
        {
            return false;
        }

        currentDurability = Mathf.Max(0, currentDurability - 1);
        if (currentDurability > 0)
        {
            return true;
        }

        string brokenName = equippedProfile.EngravingName;
        UnequipVisualOnly();
        equippedProfile = null;

        Debug.Log(
            $"<color=#FF8A65><b>【即席武器破損】</b></color> " +
            $"銘：{brokenName} は耐久切れで粉々になった！");
        return false;
    }

    /// <summary>装備ビジュアルのみを破棄します（インベントリ同期は呼び出し元）。</summary>
    public void UnequipVisualOnly()
    {
        if (weaponVisualObject != null)
        {
            Destroy(weaponVisualObject);
            weaponVisualObject = null;
        }
    }

    private void OnDestroy()
    {
        UnequipVisualOnly();
    }

    private void RebuildVisual(Transform visualParent)
    {
        UnequipVisualOnly();

        weaponVisualObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        weaponVisualObject.name = VisualChildName;
        weaponVisualObject.transform.SetParent(visualParent, worldPositionStays: false);
        weaponVisualObject.transform.localPosition = weaponLocalPosition;
        weaponVisualObject.transform.localRotation = Quaternion.Euler(weaponLocalEuler);
        weaponVisualObject.transform.localScale = weaponLocalScale;

        Collider weaponCollider = weaponVisualObject.GetComponent<Collider>();
        if (weaponCollider != null)
        {
            Destroy(weaponCollider);
        }

        Renderer renderer = weaponVisualObject.GetComponent<Renderer>();
        if (renderer != null)
        {
            Color weaponColor = string.Equals(equippedProfile.ItemId, InstantCraftWeaponItemIds.BoneSpear, StringComparison.Ordinal)
                ? new Color(0.92f, 0.9f, 0.82f, 1f)
                : new Color(0.55f, 0.58f, 0.62f, 1f);
            RuntimeUrpMaterialUtility.ApplyOpaqueColor(renderer, weaponColor);
        }
    }
}

/// <summary>Play 開始時に PlayerRobot へ即席武器装備コンポーネントを配置します。</summary>
public static class PlayerInstantWeaponLoadoutBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToPlayerRobot()
    {
        GameObject player = GameObject.Find("PlayerRobot");
        if (player == null)
        {
            player = GameObject.Find("PlayerRobot ");
        }

        if (player != null)
        {
            PlayerInstantWeaponLoadout.EnsureOn(player.transform);
        }
    }
}
