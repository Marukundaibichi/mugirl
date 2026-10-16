namespace Mugirl.Features.WeaponWheel;

/// <summary>槽位的纯计算。记录视图与双列表输入使用同一算法；视图是结构体，调用不创建投影列表或委托。</summary>
internal static class WeaponWheelSlotMath
{
    internal static void Resize<T>(List<T> list, int count, T filler)
    {
        while (list.Count < count)
        {
            list.Add(filler);
        }
        if (list.Count > count)
        {
            list.RemoveRange(count, list.Count - count);
        }
    }

    internal static int CountOccupiedUnlocked<T>(IReadOnlyList<T?> slots, IReadOnlyList<bool> unlocked) where T : class =>
        CountOccupiedUnlocked<T, ParallelSlots<T>>(new(slots, unlocked));

    internal static int CountOccupiedUnlocked(IReadOnlyList<WeaponWheelSlot> slots) =>
        CountOccupiedUnlocked<ThingWithComps, RecordSlots>(new(slots));

    internal static int FirstUnlockedEmpty<T>(IReadOnlyList<T?> slots, IReadOnlyList<bool> unlocked, int startIndex) where T : class =>
        FirstUnlockedEmpty<T, ParallelSlots<T>>(new(slots, unlocked), startIndex);

    internal static int FirstUnlockedEmpty(IReadOnlyList<WeaponWheelSlot> slots, int startIndex) =>
        FirstUnlockedEmpty<ThingWithComps, RecordSlots>(new(slots), startIndex);

    internal static int FindNextCyclic<T>(IReadOnlyList<T?> slots, IReadOnlyList<bool> unlocked, int currentSlot, Func<T, bool> accept) where T : class =>
        FindNextCyclic<T, ParallelSlots<T>>(new(slots, unlocked), currentSlot, accept);

    internal static int FindNextCyclic(IReadOnlyList<WeaponWheelSlot> slots, int currentSlot, Func<ThingWithComps, bool> accept) =>
        FindNextCyclic<ThingWithComps, RecordSlots>(new(slots), currentSlot, accept);

    internal static bool TryRotateToFront<T>(IReadOnlyList<T?> slots, IReadOnlyList<bool> unlocked, int promotedSlot, List<int> occupiedIndices, List<T?> rotated) where T : class =>
        TryRotateToFront<T, ParallelSlots<T>>(new(slots, unlocked), promotedSlot, occupiedIndices, rotated);

    internal static bool TryRotateToFront(IReadOnlyList<WeaponWheelSlot> slots, int promotedSlot, List<int> occupiedIndices, List<ThingWithComps?> rotated) =>
        TryRotateToFront<ThingWithComps, RecordSlots>(new(slots), promotedSlot, occupiedIndices, rotated);

    private static int CountOccupiedUnlocked<T, TView>(TView slots) where T : class where TView : struct, ISlots<T>
    {
        int count = 0;
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots.Unlocked(i) && slots.Item(i) != null)
            {
                count++;
            }
        }
        return count;
    }

    private static int FirstUnlockedEmpty<T, TView>(TView slots, int startIndex) where T : class where TView : struct, ISlots<T>
    {
        for (int i = Math.Max(0, startIndex); i < slots.Count; i++)
        {
            if (slots.Unlocked(i) && slots.Item(i) == null)
            {
                return i;
            }
        }
        return -1;
    }

    private static int FindNextCyclic<T, TView>(TView slots, int currentSlot, Func<T, bool> accept) where T : class where TView : struct, ISlots<T>
    {
        int count = slots.Count;
        for (int offset = 1; offset < count; offset++)
        {
            int index = (currentSlot + offset) % count;
            T? item = slots.Item(index);
            if (slots.Unlocked(index) && item != null && accept(item))
            {
                return index;
            }
        }
        return -1;
    }

    private static bool TryRotateToFront<T, TView>(TView slots, int promotedSlot, List<int> occupiedIndices, List<T?> rotated)
        where T : class where TView : struct, ISlots<T>
    {
        occupiedIndices.Clear();
        rotated.Clear();
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots.Unlocked(i) && slots.Item(i) != null)
            {
                occupiedIndices.Add(i);
            }
        }
        int promotedPosition = occupiedIndices.IndexOf(promotedSlot);
        if (occupiedIndices.Count < 2 || promotedPosition < 0 || occupiedIndices[0] != 0)
        {
            return false;
        }
        for (int offset = 0; offset < occupiedIndices.Count; offset++)
        {
            int sourcePosition = (promotedPosition + offset) % occupiedIndices.Count;
            rotated.Add(slots.Item(occupiedIndices[sourcePosition]));
        }
        return true;
    }

    private interface ISlots<T> where T : class
    {
        int Count { get; }
        T? Item(int index);
        bool Unlocked(int index);
    }

    private readonly struct ParallelSlots<T>(IReadOnlyList<T?> slots, IReadOnlyList<bool> unlocked) : ISlots<T> where T : class
    {
        public int Count => slots.Count;
        public T? Item(int index) => slots[index];
        public bool Unlocked(int index) => unlocked[index];
    }

    private readonly struct RecordSlots(IReadOnlyList<WeaponWheelSlot> slots) : ISlots<ThingWithComps>
    {
        public int Count => slots.Count;
        public ThingWithComps? Item(int index) => slots[index].Weapon;
        public bool Unlocked(int index) => slots[index].Unlocked;
    }
}
