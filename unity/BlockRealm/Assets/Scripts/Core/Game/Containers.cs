using System;
using System.Collections.Generic;
using BlockRealm.Net;

namespace BlockRealm.Sim
{
    public static class Stacks
    {
        public static bool Same(ItemStack a, ItemStack b) { return !a.IsEmpty && !b.IsEmpty && a.Id == b.Id && a.Wear == b.Wear; }

        /// <summary>Moves as much of src into dst as fits. Returns the amount moved.</summary>
        public static int Merge(ref ItemStack dst, ref ItemStack src)
        {
            if (src.IsEmpty) return 0;
            if (dst.IsEmpty)
            {
                dst = src; src = ItemStack.Empty;
                return dst.Count;
            }
            if (!Same(dst, src)) return 0;
            int room = dst.MaxStack - dst.Count;
            int move = Math.Min(room, src.Count);
            if (move <= 0) return 0;
            dst.Count = (byte)(dst.Count + move);
            src.Count = (byte)(src.Count - move);
            if (src.Count == 0) src = ItemStack.Empty;
            return move;
        }
    }

    /// <summary>A player's 36-slot inventory (0..8 is the hotbar) and the item held on the cursor.</summary>
    public sealed class PlayerInventory
    {
        public const int Size = 36;
        public const int HotbarSize = 9;
        public readonly ItemStack[] Slots = new ItemStack[Size];
        /// <summary>Worn pieces: 0 head, 1 chest, 2 legs, 3 feet.</summary>
        public readonly ItemStack[] Armor = new ItemStack[4];
        public ItemStack Cursor;
        public int Selected;

        public ItemStack Held { get { return Slots[Selected]; } }

        /// <summary>Total protection of everything worn (each point removes 4% of an incoming blow, up to 20).</summary>
        public int ArmorPoints()
        {
            int points = 0;
            for (int i = 0; i < Armor.Length; i++)
                if (!Armor[i].IsEmpty && Armor[i].Info.ArmorSlot == i) points += Armor[i].Info.ArmorPoints;
            return Math.Min(points, 20);
        }

        /// <summary>Adds a stack, preferring existing stacks and the hotbar. Returns how many items did not fit.</summary>
        public int Add(ItemStack stack)
        {
            if (stack.IsEmpty) return 0;
            for (int i = 0; i < Size && !stack.IsEmpty; i++)
            {
                if (Stacks.Same(Slots[i], stack)) Stacks.Merge(ref Slots[i], ref stack);
            }
            for (int i = 0; i < Size && !stack.IsEmpty; i++)
            {
                if (Slots[i].IsEmpty) Stacks.Merge(ref Slots[i], ref stack);
            }
            return stack.IsEmpty ? 0 : stack.Count;
        }

        public bool CanFit(ItemStack stack)
        {
            int left = stack.Count;
            for (int i = 0; i < Size && left > 0; i++)
            {
                if (Slots[i].IsEmpty) return true;
                if (Stacks.Same(Slots[i], stack)) left -= Slots[i].MaxStack - Slots[i].Count;
            }
            return left <= 0;
        }

        public int Count(ushort id)
        {
            int n = 0;
            for (int i = 0; i < Size; i++) if (Slots[i].Id == id) n += Slots[i].Count;
            return n;
        }

        public bool Remove(ushort id, int count)
        {
            if (Count(id) < count) return false;
            for (int i = 0; i < Size && count > 0; i++)
            {
                if (Slots[i].Id != id) continue;
                int take = Math.Min(count, Slots[i].Count);
                Slots[i].Count = (byte)(Slots[i].Count - take);
                if (Slots[i].Count == 0) Slots[i] = ItemStack.Empty;
                count -= take;
            }
            return true;
        }

        /// <summary>Removes one item from the selected hotbar slot.</summary>
        public void ConsumeHeld()
        {
            ItemStack s = Slots[Selected];
            if (s.IsEmpty) return;
            s.Count--;
            Slots[Selected] = s.Count == 0 ? ItemStack.Empty : s;
        }

        /// <summary>Wears the held tool by one use; breaks it at the end of its durability.</summary>
        public bool WearHeld(int amount = 1)
        {
            ItemStack s = Slots[Selected];
            ItemInfo info = s.Info;
            if (s.IsEmpty || info.Durability <= 0) return false;
            s.Wear = (short)(s.Wear + amount);
            if (s.Wear >= info.Durability) { Slots[Selected] = ItemStack.Empty; return true; }
            Slots[Selected] = s;
            return false;
        }
    }

    public sealed class FurnaceState
    {
        public const int CookTicks = 200;
        public ItemStack Input, Fuel, Output;
        public int BurnLeft, BurnTotal, Progress;

        public bool IsBurning { get { return BurnLeft > 0; } }

        private bool CanSmelt()
        {
            if (Input.IsEmpty) return false;
            ItemStack result;
            if (!Recipes.TrySmelt(Input.Id, out result)) return false;
            if (Output.IsEmpty) return true;
            return Output.Id == result.Id && Output.Count + result.Count <= Output.MaxStack;
        }

        /// <summary>Advances the furnace one tick. Returns true when its lit state changed (the block should be swapped).</summary>
        public bool Tick()
        {
            bool wasBurning = BurnLeft > 0;
            bool canSmelt = CanSmelt();
            if (BurnLeft > 0) BurnLeft--;
            if (BurnLeft == 0 && canSmelt && !Fuel.IsEmpty && Fuel.Info.FuelTicks > 0)
            {
                BurnTotal = BurnLeft = Fuel.Info.FuelTicks;
                Fuel.Count--;
                if (Fuel.Count == 0) Fuel = ItemStack.Empty;
            }
            if (BurnLeft > 0 && canSmelt)
            {
                Progress++;
                if (Progress >= CookTicks)
                {
                    Progress = 0;
                    ItemStack result;
                    Recipes.TrySmelt(Input.Id, out result);
                    if (Output.IsEmpty) Output = result; else Output.Count = (byte)(Output.Count + result.Count);
                    Input.Count--;
                    if (Input.Count == 0) Input = ItemStack.Empty;
                }
            }
            else if (!canSmelt)
            {
                Progress = 0;
            }
            return wasBurning != (BurnLeft > 0);
        }
    }

    public sealed class ChestState
    {
        public const int Size = 27;
        public readonly ItemStack[] Slots = new ItemStack[Size];
    }

    /// <summary>
    /// An open inventory window. Slots 0..35 are always the player's inventory; further slots belong to the
    /// window kind (crafting grid, furnace, chest). All rules live here so server and tests share them.
    /// </summary>
    public abstract class Window
    {
        public readonly byte Id;
        public readonly WindowKind Kind;
        public readonly PlayerInventory Inv;

        protected Window(byte id, WindowKind kind, PlayerInventory inv) { Id = id; Kind = kind; Inv = inv; }

        public abstract int ExtraCount { get; }
        protected abstract ItemStack GetExtra(int i);
        protected abstract void SetExtra(int i, ItemStack s);
        protected virtual bool ExtraAccepts(int i, ItemStack s) { return true; }
        protected virtual bool IsResult(int i) { return false; }
        protected virtual bool IsOutput(int i) { return false; }
        protected virtual void OnExtraChanged(int i) { }
        /// <summary>Where shift-click sends items from the given (player) slot: [start, end) in extra slots, or empty.</summary>
        protected virtual void QuickTargetFor(ItemStack s, out int start, out int end) { start = 0; end = 0; }

        public int SlotCount { get { return PlayerInventory.Size + ExtraCount; } }

        public ItemStack Get(int slot)
        {
            if (slot < 0 || slot >= SlotCount) return ItemStack.Empty;
            return slot < PlayerInventory.Size ? Inv.Slots[slot] : GetExtra(slot - PlayerInventory.Size);
        }

        private void Set(int slot, ItemStack s)
        {
            if (slot < PlayerInventory.Size) Inv.Slots[slot] = s;
            else { SetExtra(slot - PlayerInventory.Size, s); OnExtraChanged(slot - PlayerInventory.Size); }
        }

        private bool Accepts(int slot, ItemStack s)
        {
            if (slot < PlayerInventory.Size) return true;
            int e = slot - PlayerInventory.Size;
            if (IsResult(e) || IsOutput(e)) return false;
            return ExtraAccepts(e, s);
        }

        public ItemStack[] Snapshot()
        {
            ItemStack[] all = new ItemStack[SlotCount];
            for (int i = 0; i < all.Length; i++) all[i] = Get(i);
            return all;
        }

        /// <summary>Applies one click with Minecraft's cursor semantics. Returns false if the slot is invalid.</summary>
        public bool Click(int slot, int button, bool shift)
        {
            if (slot < 0 || slot >= SlotCount) return false;
            int extra = slot - PlayerInventory.Size;
            if (slot >= PlayerInventory.Size && IsResult(extra)) { ClickResult(slot, shift); return true; }

            if (shift) { QuickMove(slot); return true; }

            ItemStack s = Get(slot);
            ItemStack cur = Inv.Cursor;
            bool outputSlot = slot >= PlayerInventory.Size && IsOutput(extra);

            if (outputSlot)
            {
                // Take-only: pick up (or add to the cursor), never place.
                if (s.IsEmpty) return true;
                if (cur.IsEmpty) { Inv.Cursor = s; Set(slot, ItemStack.Empty); }
                else if (Stacks.Same(cur, s) && cur.Count + s.Count <= cur.MaxStack)
                {
                    cur.Count = (byte)(cur.Count + s.Count); Inv.Cursor = cur; Set(slot, ItemStack.Empty);
                }
                return true;
            }

            if (button == 0)
            {
                if (cur.IsEmpty) { Inv.Cursor = s; Set(slot, ItemStack.Empty); }
                else if (!Accepts(slot, cur)) { }
                else if (s.IsEmpty) { Set(slot, cur); Inv.Cursor = ItemStack.Empty; }
                else if (Stacks.Same(s, cur)) { Stacks.Merge(ref s, ref cur); Set(slot, s); Inv.Cursor = cur; }
                else { Set(slot, cur); Inv.Cursor = s; }
            }
            else
            {
                if (cur.IsEmpty)
                {
                    if (s.IsEmpty) return true;
                    int take = (s.Count + 1) / 2;
                    ItemStack pick = s; pick.Count = (byte)take;
                    s.Count = (byte)(s.Count - take);
                    Set(slot, s.Count == 0 ? ItemStack.Empty : s);
                    Inv.Cursor = pick;
                }
                else if (!Accepts(slot, cur)) { }
                else if (s.IsEmpty)
                {
                    ItemStack one = cur; one.Count = 1;
                    Set(slot, one);
                    cur.Count--; Inv.Cursor = cur.Count == 0 ? ItemStack.Empty : cur;
                }
                else if (Stacks.Same(s, cur) && s.Count < s.MaxStack)
                {
                    s.Count++; Set(slot, s);
                    cur.Count--; Inv.Cursor = cur.Count == 0 ? ItemStack.Empty : cur;
                }
                else if (!Stacks.Same(s, cur)) { Set(slot, cur); Inv.Cursor = s; }
            }
            return true;
        }

        protected virtual void ClickResult(int slot, bool shift) { }

        private void QuickMove(int slot)
        {
            ItemStack s = Get(slot);
            if (s.IsEmpty) return;
            int extra = slot - PlayerInventory.Size;
            if (slot >= PlayerInventory.Size)
            {
                // From the window into the player's inventory.
                if (!InsertRange(ref s, 0, PlayerInventory.Size, true)) { }
                Set(slot, s);
                return;
            }
            int start, end;
            QuickTargetFor(s, out start, out end);
            if (end > start)
            {
                InsertRange(ref s, PlayerInventory.Size + start, PlayerInventory.Size + end, false);
                Set(slot, s);
                return;
            }
            // Within the player's inventory: hotbar <-> main.
            if (slot < PlayerInventory.HotbarSize) InsertRange(ref s, PlayerInventory.HotbarSize, PlayerInventory.Size, false);
            else InsertRange(ref s, 0, PlayerInventory.HotbarSize, false);
            Set(slot, s);
        }

        private bool InsertRange(ref ItemStack s, int start, int end, bool reverse)
        {
            for (int pass = 0; pass < 2 && !s.IsEmpty; pass++)
            {
                for (int k = 0; k < end - start && !s.IsEmpty; k++)
                {
                    int i = reverse ? end - 1 - k : start + k;
                    ItemStack d = Get(i);
                    if (!Accepts(i, s)) continue;
                    if (pass == 0 ? !Stacks.Same(d, s) : !d.IsEmpty) continue;
                    Stacks.Merge(ref d, ref s);
                    Set(i, d);
                }
            }
            return s.IsEmpty;
        }

        /// <summary>Items that must go back to the player when the window closes.</summary>
        public virtual List<ItemStack> TakeLeftovers() { return new List<ItemStack>(); }
    }

    /// <summary>The player's own window, with the 2x2 crafting grid, or a crafting table's 3x3 grid.</summary>
    public sealed class CraftingWindow : Window
    {
        public readonly int Dim;
        public readonly ItemStack[] Grid;
        private ItemStack _result;

        public CraftingWindow(byte id, WindowKind kind, PlayerInventory inv, int dim) : base(id, kind, inv)
        {
            Dim = dim; Grid = new ItemStack[dim * dim];
        }

        /// <summary>The player's own window also carries the four armour slots, after the grid and result.</summary>
        private bool HasArmor { get { return Dim == 2; } }

        public override int ExtraCount { get { return Grid.Length + 1 + (HasArmor ? 4 : 0); } }
        protected override ItemStack GetExtra(int i)
        {
            if (i < Grid.Length) return Grid[i];
            if (i == Grid.Length) return _result;
            return Inv.Armor[i - Grid.Length - 1];
        }
        protected override void SetExtra(int i, ItemStack s)
        {
            if (i < Grid.Length) Grid[i] = s;
            else if (i == Grid.Length) _result = s;
            else Inv.Armor[i - Grid.Length - 1] = s;
        }
        protected override bool IsResult(int i) { return i == Grid.Length; }
        protected override bool ExtraAccepts(int i, ItemStack s)
        {
            if (i <= Grid.Length) return true;
            return !s.IsEmpty && s.Info.ArmorSlot == i - Grid.Length - 1;
        }
        protected override void OnExtraChanged(int i) { Recompute(); }
        protected override void QuickTargetFor(ItemStack s, out int start, out int end)
        {
            if (HasArmor && s.Info.ArmorSlot >= 0) { start = Grid.Length + 1; end = start + 4; }
            else { start = 0; end = Grid.Length; }
        }

        public void Recompute() { _result = Recipes.Match(Grid, Dim, Dim); }

        protected override void ClickResult(int slot, bool shift)
        {
            for (int guard = 0; guard < 64; guard++)
            {
                Recompute();
                ItemStack res = _result;
                if (res.IsEmpty) return;
                if (shift)
                {
                    if (!Inv.CanFit(res)) return;
                    Inv.Add(res);
                }
                else
                {
                    ItemStack cur = Inv.Cursor;
                    if (cur.IsEmpty) Inv.Cursor = res;
                    else if (Stacks.Same(cur, res) && cur.Count + res.Count <= cur.MaxStack) { cur.Count = (byte)(cur.Count + res.Count); Inv.Cursor = cur; }
                    else return;
                }
                for (int i = 0; i < Grid.Length; i++)
                {
                    if (Grid[i].IsEmpty) continue;
                    Grid[i].Count--;
                    if (Grid[i].Count == 0) Grid[i] = ItemStack.Empty;
                }
                Recompute();
                if (!shift) return;
            }
        }

        public override List<ItemStack> TakeLeftovers()
        {
            List<ItemStack> list = new List<ItemStack>();
            for (int i = 0; i < Grid.Length; i++)
            {
                if (!Grid[i].IsEmpty) list.Add(Grid[i]);
                Grid[i] = ItemStack.Empty;
            }
            _result = ItemStack.Empty;
            return list;
        }
    }

    public sealed class FurnaceWindow : Window
    {
        public readonly FurnaceState State;
        public FurnaceWindow(byte id, PlayerInventory inv, FurnaceState state) : base(id, WindowKind.Furnace, inv) { State = state; }

        public override int ExtraCount { get { return 3; } }
        protected override ItemStack GetExtra(int i) { return i == 0 ? State.Input : (i == 1 ? State.Fuel : State.Output); }
        protected override void SetExtra(int i, ItemStack s) { if (i == 0) State.Input = s; else if (i == 1) State.Fuel = s; else State.Output = s; }
        protected override bool IsOutput(int i) { return i == 2; }
        protected override bool ExtraAccepts(int i, ItemStack s)
        {
            if (i == 1) return s.Info.FuelTicks > 0;
            return true;
        }
        protected override void QuickTargetFor(ItemStack s, out int start, out int end)
        {
            if (s.Info.FuelTicks > 0 && !Recipes.TrySmeltCheck(s.Id)) { start = 1; end = 2; }
            else { start = 0; end = 1; }
        }
    }

    public sealed class ChestWindow : Window
    {
        public readonly ChestState State;
        public ChestWindow(byte id, PlayerInventory inv, ChestState state) : base(id, WindowKind.Chest, inv) { State = state; }
        public override int ExtraCount { get { return ChestState.Size; } }
        protected override ItemStack GetExtra(int i) { return State.Slots[i]; }
        protected override void SetExtra(int i, ItemStack s) { State.Slots[i] = s; }
        protected override void QuickTargetFor(ItemStack s, out int start, out int end) { start = 0; end = ChestState.Size; }
    }
}
