namespace Mugirl.Features.WeaponWheel;

/// <summary>固定登记顺序的兼容回调。所有判定短路；tick 回调只在领域 tick 正常返回后执行。</summary>
internal sealed class WeaponWheelHookSet
{
    private readonly HashSet<string> registrations = new(StringComparer.Ordinal);
    private readonly HashSet<string> owners = new(StringComparer.Ordinal);
    private readonly List<Predicate<Pawn?>> eligibilityVetoes = [];
    private readonly List<Predicate<Pawn?>> castPassThrough = [];
    private readonly List<Predicate<Pawn?>> combatBypasses = [];
    private readonly List<Predicate<ThingWithComps?>> foreignEquipment = [];
    private readonly List<IWeaponWheelPrimarySwapGuard> primaryGuards = [];
    private readonly List<Action<Pawn?>> primaryListeners = [];
    private readonly List<Action<CompWeaponWheel>> tickers = [];
    private readonly List<Action<Pawn?, ThingWithComps?>> initializers = [];
    private Func<Thing, Vector3>? bodyPosition;

    internal bool IsRegistered(string owner) => owners.Contains(owner);

    internal void AddEligibilityVeto(string owner, Predicate<Pawn?> callback) => Add(owner, "eligibility", callback, eligibilityVetoes);
    internal void AddCastPassThrough(string owner, Predicate<Pawn?> callback) => Add(owner, "cast", callback, castPassThrough);
    internal void AddCombatBypass(string owner, Predicate<Pawn?> callback) => Add(owner, "bypass", callback, combatBypasses);
    internal void AddForeignEquipmentFilter(string owner, Predicate<ThingWithComps?> callback) => Add(owner, "equipment", callback, foreignEquipment);
    internal void AddPrimarySwapGuard(string owner, IWeaponWheelPrimarySwapGuard guard) => Add(owner, "swap", guard, primaryGuards);
    internal void AddPrimaryChangedListener(string owner, Action<Pawn?> callback) => Add(owner, "changed", callback, primaryListeners);
    internal void AddTickCompleted(string owner, Action<CompWeaponWheel> callback) => Add(owner, "tick", callback, tickers);
    internal void AddGeneratedWeaponInitializer(string owner, Action<Pawn?, ThingWithComps?> callback) => Add(owner, "generated", callback, initializers);

    internal void SetBodyPosition(string owner, Func<Thing, Vector3> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (registrations.Contains(owner + "/position"))
        {
            return;
        }
        if (bodyPosition is not null)
        {
            throw new InvalidOperationException("Only one weapon-wheel body-position provider can be registered.");
        }
        Register(owner, "position");
        bodyPosition = callback;
    }

    internal bool IsEligibilityVetoed(Pawn? pawn) => Any(eligibilityVetoes, pawn);
    internal bool ShouldPassCastThrough(Pawn? pawn) => Any(castPassThrough, pawn);
    internal bool ShouldBypassCombat(Pawn? pawn) => Any(combatBypasses, pawn);
    internal bool IsForeignEquipment(ThingWithComps? weapon) => Any(foreignEquipment, weapon);
    internal Vector3 BodyPosition(Thing thing) => bodyPosition is { } provider ? provider(thing) : thing.DrawPos.ToNumerics();

    internal void TickCompleted(CompWeaponWheel wheel)
    {
        for (int i = 0; i < tickers.Count; i++)
        {
            tickers[i](wheel);
        }
    }

    internal void InitializeGeneratedWeapon(Pawn? pawn, ThingWithComps? weapon)
    {
        for (int i = 0; i < initializers.Count; i++)
        {
            initializers[i](pawn, weapon);
        }
    }

    internal bool TryBeginPrimarySwap(CompWeaponWheel wheel, int targetSlot, out PrimarySwapScope scope)
    {
        scope = new PrimarySwapScope(wheel, primaryGuards, primaryListeners);
        // The original Dual Wield Prefix queried WeaponAt only while that integration was active.
        if (primaryGuards.Count != 0 && wheel.Pawn?.equipment?.Primary != wheel.WeaponAt(targetSlot))
        {
            scope.PrepareTokens();
            for (int i = 0; i < primaryGuards.Count; i++)
            {
                bool proceed = primaryGuards[i].TryBeginPrimarySwap(wheel, out object? token);
                scope.SetToken(i, token);
                if (!proceed)
                {
                    // Harmony still runs the observation-only Melee Animation Prefix when Dual Wield skips the body.
                    scope.CapturePrimary();
                    return false;
                }
            }
        }
        // Melee Animation's lower-priority Prefix ran after off-hand detachment, not at facade entry.
        scope.CapturePrimary();
        return true;
    }

    private void Add<T>(string owner, string endpoint, T callback, List<T> target) where T : class
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (Register(owner, endpoint))
        {
            target.Add(callback);
        }
    }

    private bool Register(string owner, string endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        owners.Add(owner);
        return registrations.Add(owner + "/" + endpoint);
    }

    private static bool Any<T>(List<Predicate<T>> callbacks, T argument)
    {
        for (int i = 0; i < callbacks.Count; i++)
        {
            if (callbacks[i](argument))
            {
                return true;
            }
        }
        return false;
    }

    internal struct PrimarySwapScope
    {
        private readonly CompWeaponWheel? wheel;
        private readonly List<IWeaponWheelPrimarySwapGuard>? guards;
        private readonly List<Action<Pawn?>>? listeners;
        private object?[]? tokens;
        private ThingWithComps? primaryBefore;
        private bool restored;

        internal PrimarySwapScope(CompWeaponWheel wheel, List<IWeaponWheelPrimarySwapGuard> guards, List<Action<Pawn?>> listeners)
        {
            this.wheel = wheel;
            this.guards = guards;
            this.listeners = listeners;
        }

        internal void PrepareTokens() => tokens = new object?[guards!.Count];
        internal void SetToken(int index, object? token) => tokens![index] = token;
        internal void CapturePrimary()
        {
            if (listeners!.Count != 0)
            {
                primaryBefore = wheel?.Pawn?.equipment?.Primary;
            }
        }

        internal void Restore()
        {
            if (restored)
            {
                return;
            }
            restored = true;
            if (tokens is null)
            {
                return;
            }
            for (int i = 0; i < tokens.Length; i++)
            {
                if (tokens[i] is { } token)
                {
                    guards![i].EndPrimarySwap(wheel!, token);
                }
            }
        }

        internal void NotifySucceeded(bool succeeded)
        {
            if (!succeeded || listeners is null || listeners.Count == 0)
            {
                return;
            }
            Pawn? pawn = wheel?.Pawn;
            if (pawn?.equipment?.Primary != primaryBefore)
            {
                for (int i = 0; i < listeners.Count; i++)
                {
                    listeners[i](pawn);
                }
            }
        }
    }
}
