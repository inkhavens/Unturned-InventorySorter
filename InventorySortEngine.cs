using System;
using System.Collections.Generic;
using SDG.Unturned;
using UnityEngine;

namespace InventorySorter
{
    public enum SortResult
    {
        Success,
        NothingToSort,
        CouldNotPack,
        ApplyFailed
    }

    public static class InventorySortEngine
    {
        public static InteractableStorage GetLookedAtStorage(Player player)
        {
            if (player == null || player.look == null || player.look.aim == null)
            {
                return null;
            }

            RaycastHit hit;
            Ray ray = new Ray(
                player.look.aim.position,
                player.look.aim.forward);
            float fallbackDistance = 6f;
            if (Physics.Raycast(ray, out hit, 6f, RayMasks.PLAYER_INTERACT))
            {
                fallbackDistance = Math.Min(6f, hit.distance + 0.6f);
                InteractableStorage direct = GetStorage(hit.transform);
                if (direct != null)
                {
                    return direct;
                }
            }

            RaycastHit[] nearbyHits = Physics.SphereCastAll(
                ray,
                0.4f,
                fallbackDistance,
                RayMasks.PLAYER_INTERACT);
            Array.Sort(nearbyHits, delegate(RaycastHit a, RaycastHit b)
            {
                return a.distance.CompareTo(b.distance);
            });

            foreach (RaycastHit nearbyHit in nearbyHits)
            {
                InteractableStorage storage = GetStorage(nearbyHit.transform);
                if (storage != null)
                {
                    return storage;
                }
            }

            return null;
        }

        private static InteractableStorage GetStorage(Transform transform)
        {
            return transform == null
                ? null
                : transform.GetComponentInParent<InteractableStorage>();
        }

        public static SortResult SortInventory(Player player, out int itemCount)
        {
            itemCount = 0;
            if (player == null || player.inventory == null)
            {
                return SortResult.ApplyFailed;
            }

            List<Items> pages = new List<Items>();
            for (byte page = PlayerInventory.SLOTS;
                 page < PlayerInventory.STORAGE;
                 page++)
            {
                Items items = player.inventory.items[page];
                if (items != null && items.width > 0 && items.height > 0)
                {
                    pages.Add(items);
                    itemCount += items.getItemCount();
                }
            }

            if (itemCount < 2)
            {
                return SortResult.NothingToSort;
            }

            return SortPages(pages);
        }

        public static SortResult SortStorage(InteractableStorage storage,
                                             out int itemCount)
        {
            itemCount = 0;
            if (storage == null || storage.items == null)
            {
                return SortResult.ApplyFailed;
            }

            itemCount = storage.items.getItemCount();
            if (itemCount < 2)
            {
                return SortResult.NothingToSort;
            }

            return SortPages(new List<Items> { storage.items });
        }

        private static SortResult SortPages(List<Items> pages)
        {
            List<ItemRecord> records = Capture(pages);
            List<Placement> plan;
            if (!TryCreateBestPlan(pages, records, out plan))
            {
                return SortResult.CouldNotPack;
            }

            List<Placement> original = CaptureOriginal(records);
            if (ApplyAndVerify(pages, plan, records))
            {
                return SortResult.Success;
            }

            ApplyAndVerify(pages, original, records);
            return SortResult.ApplyFailed;
        }

        private static List<ItemRecord> Capture(List<Items> pages)
        {
            List<ItemRecord> result = new List<ItemRecord>();
            int ordinal = 0;

            foreach (Items page in pages)
            {
                foreach (ItemJar jar in page.items)
                {
                    ItemAsset asset = jar.item.GetAsset();
                    result.Add(new ItemRecord
                    {
                        Item = jar.item,
                        Type = asset == null ? int.MaxValue : (int)asset.type,
                        Rarity = asset == null ? int.MaxValue : (int)asset.rarity,
                        Width = jar.size_x,
                        Height = jar.size_y,
                        OriginalPage = page,
                        OriginalX = jar.x,
                        OriginalY = jar.y,
                        OriginalRotation = jar.rot,
                        Ordinal = ordinal++
                    });
                }
            }

            return result;
        }

        private static List<Placement> CaptureOriginal(List<ItemRecord> records)
        {
            List<Placement> result = new List<Placement>();
            foreach (ItemRecord record in records)
            {
                result.Add(new Placement
                {
                    Record = record,
                    Page = record.OriginalPage,
                    X = record.OriginalX,
                    Y = record.OriginalY,
                    Rotation = record.OriginalRotation
                });
            }

            return result;
        }

        private static bool TryCreateBestPlan(
            List<Items> pages,
            List<ItemRecord> records,
            out List<Placement> bestPlan)
        {
            bestPlan = null;
            PlanScore bestScore = null;

            for (int strategy = 0; strategy < 6; strategy++)
            {
                List<ItemRecord> ordered = new List<ItemRecord>(records);
                ordered.Sort(delegate(ItemRecord a, ItemRecord b)
                {
                    return CompareItems(a, b, strategy);
                });

                List<Placement> candidate;
                if (!TryPack(pages, ordered, out candidate))
                {
                    continue;
                }

                PlanScore score = Score(pages, candidate);
                if (bestScore == null || score.CompareTo(bestScore) < 0)
                {
                    bestScore = score;
                    bestPlan = candidate;
                }
            }

            return bestPlan != null;
        }

        private static int CompareItems(ItemRecord a, ItemRecord b, int strategy)
        {
            int areaA = a.Width * a.Height;
            int areaB = b.Width * b.Height;
            int longA = Math.Max(a.Width, a.Height);
            int longB = Math.Max(b.Width, b.Height);
            int shortA = Math.Min(a.Width, a.Height);
            int shortB = Math.Min(b.Width, b.Height);
            int comparison;

            if (strategy == 0)
            {
                comparison = a.Type.CompareTo(b.Type);
                if (comparison == 0) comparison = a.Item.id.CompareTo(b.Item.id);
                if (comparison == 0) comparison = areaB.CompareTo(areaA);
            }
            else if (strategy == 1)
            {
                comparison = a.Type.CompareTo(b.Type);
                if (comparison == 0) comparison = a.Rarity.CompareTo(b.Rarity);
                if (comparison == 0) comparison = a.Item.id.CompareTo(b.Item.id);
            }
            else if (strategy == 2)
            {
                comparison = a.Type.CompareTo(b.Type);
                if (comparison == 0) comparison = areaB.CompareTo(areaA);
                if (comparison == 0) comparison = a.Item.id.CompareTo(b.Item.id);
            }
            else if (strategy == 3)
            {
                comparison = a.Item.id.CompareTo(b.Item.id);
                if (comparison == 0) comparison = areaB.CompareTo(areaA);
            }
            else if (strategy == 4)
            {
                comparison = areaB.CompareTo(areaA);
                if (comparison == 0) comparison = a.Type.CompareTo(b.Type);
                if (comparison == 0) comparison = a.Item.id.CompareTo(b.Item.id);
            }
            else
            {
                comparison = longB.CompareTo(longA);
                if (comparison == 0) comparison = a.Type.CompareTo(b.Type);
                if (comparison == 0) comparison = a.Item.id.CompareTo(b.Item.id);
                if (comparison == 0) comparison = shortB.CompareTo(shortA);
            }

            return comparison != 0
                ? comparison
                : a.Ordinal.CompareTo(b.Ordinal);
        }

        private static bool TryPack(
            List<Items> pages,
            List<ItemRecord> records,
            out List<Placement> placements)
        {
            placements = new List<Placement>();
            Dictionary<Items, bool[,]> occupied =
                new Dictionary<Items, bool[,]>();

            foreach (Items page in pages)
            {
                occupied[page] = new bool[page.width, page.height];
            }

            foreach (ItemRecord record in records)
            {
                Placement placement;
                if (!TryPlaceRecord(pages, occupied, record, out placement))
                {
                    placements = null;
                    return false;
                }

                Mark(occupied[placement.Page], placement, true);
                placements.Add(placement);
            }

            return true;
        }

        private static bool TryPlaceRecord(
            List<Items> pages,
            Dictionary<Items, bool[,]> occupied,
            ItemRecord record,
            out Placement placement)
        {
            byte firstRotation = (byte)(record.OriginalRotation % 2);
            byte secondRotation = (byte)(firstRotation == 0 ? 1 : 0);
            byte[] rotations = record.Width == record.Height
                ? new[] { firstRotation }
                : new[] { firstRotation, secondRotation };

            foreach (Items page in pages)
            {
                foreach (byte rotation in rotations)
                {
                    int width = rotation % 2 == 0
                        ? record.Width
                        : record.Height;
                    int height = rotation % 2 == 0
                        ? record.Height
                        : record.Width;

                    for (int y = 0; y + height <= page.height; y++)
                    {
                        for (int x = 0; x + width <= page.width; x++)
                        {
                            if (!IsFree(occupied[page], x, y, width, height))
                            {
                                continue;
                            }

                            placement = new Placement
                            {
                                Record = record,
                                Page = page,
                                X = (byte)x,
                                Y = (byte)y,
                                Rotation = rotation
                            };
                            return true;
                        }
                    }
                }
            }

            placement = null;
            return false;
        }

        private static bool IsFree(
            bool[,] occupied,
            int x,
            int y,
            int width,
            int height)
        {
            for (int checkX = x; checkX < x + width; checkX++)
            {
                for (int checkY = y; checkY < y + height; checkY++)
                {
                    if (occupied[checkX, checkY])
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static void Mark(bool[,] occupied,
                                 Placement placement,
                                 bool value)
        {
            int width = placement.Rotation % 2 == 0
                ? placement.Record.Width
                : placement.Record.Height;
            int height = placement.Rotation % 2 == 0
                ? placement.Record.Height
                : placement.Record.Width;

            for (int x = placement.X; x < placement.X + width; x++)
            {
                for (int y = placement.Y; y < placement.Y + height; y++)
                {
                    occupied[x, y] = value;
                }
            }
        }

        private static PlanScore Score(
            List<Items> pages,
            List<Placement> placements)
        {
            Dictionary<Items, int> pageIndices = new Dictionary<Items, int>();
            for (int i = 0; i < pages.Count; i++)
            {
                pageIndices[pages[i]] = i;
            }

            int highestUsedPage = -1;
            int occupiedPages = 0;
            int extent = 0;
            int groupPenalty = CalculateGroupPenalty(
                pages,
                placements,
                pageIndices);
            HashSet<Items> usedPages = new HashSet<Items>();

            foreach (Placement placement in placements)
            {
                usedPages.Add(placement.Page);
                highestUsedPage = Math.Max(
                    highestUsedPage,
                    pageIndices[placement.Page]);
                int width = placement.Rotation % 2 == 0
                    ? placement.Record.Width
                    : placement.Record.Height;
                int height = placement.Rotation % 2 == 0
                    ? placement.Record.Height
                    : placement.Record.Width;
                extent += (placement.Y + height) * 100 + placement.X + width;
            }

            occupiedPages = usedPages.Count;
            return new PlanScore(
                groupPenalty,
                occupiedPages,
                highestUsedPage,
                extent);
        }

        private static int CalculateGroupPenalty(
            List<Items> pages,
            List<Placement> placements,
            Dictionary<Items, int> pageIndices)
        {
            Dictionary<ushort, List<Placement>> groups =
                new Dictionary<ushort, List<Placement>>();
            foreach (Placement placement in placements)
            {
                List<Placement> group;
                if (!groups.TryGetValue(placement.Record.Item.id, out group))
                {
                    group = new List<Placement>();
                    groups.Add(placement.Record.Item.id, group);
                }
                group.Add(placement);
            }

            int penalty = 0;
            foreach (List<Placement> group in groups.Values)
            {
                Dictionary<Items, Bounds> boundsByPage =
                    new Dictionary<Items, Bounds>();
                foreach (Placement placement in group)
                {
                    int width = placement.Rotation % 2 == 0
                        ? placement.Record.Width
                        : placement.Record.Height;
                    int height = placement.Rotation % 2 == 0
                        ? placement.Record.Height
                        : placement.Record.Width;
                    Bounds bounds;
                    if (!boundsByPage.TryGetValue(placement.Page, out bounds))
                    {
                        bounds = new Bounds();
                        bounds.MinX = placement.X;
                        bounds.MinY = placement.Y;
                        bounds.MaxX = placement.X + width;
                        bounds.MaxY = placement.Y + height;
                        bounds.CellArea = 0;
                        boundsByPage.Add(placement.Page, bounds);
                    }

                    bounds.MinX = Math.Min(bounds.MinX, placement.X);
                    bounds.MinY = Math.Min(bounds.MinY, placement.Y);
                    bounds.MaxX = Math.Max(bounds.MaxX, placement.X + width);
                    bounds.MaxY = Math.Max(bounds.MaxY, placement.Y + height);
                    bounds.CellArea += width * height;
                }

                penalty += (boundsByPage.Count - 1) * 100000;
                foreach (Bounds bounds in boundsByPage.Values)
                {
                    int boundingArea =
                        (bounds.MaxX - bounds.MinX) *
                        (bounds.MaxY - bounds.MinY);
                    penalty += (boundingArea - bounds.CellArea) * 100;
                }
            }

            List<Placement> visualOrder = new List<Placement>(placements);
            visualOrder.Sort(delegate(Placement a, Placement b)
            {
                int result = pageIndices[a.Page].CompareTo(pageIndices[b.Page]);
                if (result == 0) result = a.Y.CompareTo(b.Y);
                if (result == 0) result = a.X.CompareTo(b.X);
                return result;
            });

            for (int i = 1; i < visualOrder.Count; i++)
            {
                if (visualOrder[i - 1].Record.Item.id !=
                    visualOrder[i].Record.Item.id)
                {
                    penalty += 10;
                }
                if (visualOrder[i - 1].Record.Type !=
                    visualOrder[i].Record.Type)
                {
                    penalty += 2;
                }
            }

            return penalty;
        }

        private static bool ApplyAndVerify(
            List<Items> pages,
            List<Placement> placements,
            List<ItemRecord> expectedRecords)
        {
            try
            {
                foreach (Items page in pages)
                {
                    for (int index = page.items.Count - 1; index >= 0; index--)
                    {
                        page.removeItem((byte)index);
                    }
                }

                foreach (Placement placement in placements)
                {
                    placement.Page.addItem(
                        placement.X,
                        placement.Y,
                        placement.Rotation,
                        placement.Record.Item);
                }

                return Verify(pages, placements, expectedRecords);
            }
            catch
            {
                return false;
            }
        }

        private static bool Verify(
            List<Items> pages,
            List<Placement> placements,
            List<ItemRecord> expectedRecords)
        {
            int count = 0;
            HashSet<Item> found = new HashSet<Item>();
            foreach (Items page in pages)
            {
                count += page.items.Count;
                foreach (ItemJar jar in page.items)
                {
                    found.Add(jar.item);
                }
            }

            if (count != expectedRecords.Count || found.Count != expectedRecords.Count)
            {
                return false;
            }

            foreach (ItemRecord record in expectedRecords)
            {
                if (!found.Contains(record.Item))
                {
                    return false;
                }
            }

            foreach (Placement placement in placements)
            {
                bool matched = false;
                foreach (ItemJar jar in placement.Page.items)
                {
                    if (ReferenceEquals(jar.item, placement.Record.Item) &&
                        jar.x == placement.X &&
                        jar.y == placement.Y &&
                        jar.rot == placement.Rotation)
                    {
                        matched = true;
                        break;
                    }
                }

                if (!matched)
                {
                    return false;
                }
            }

            return true;
        }

        private sealed class ItemRecord
        {
            public Item Item;
            public int Type;
            public int Rarity;
            public byte Width;
            public byte Height;
            public Items OriginalPage;
            public byte OriginalX;
            public byte OriginalY;
            public byte OriginalRotation;
            public int Ordinal;
        }

        private sealed class Placement
        {
            public ItemRecord Record;
            public Items Page;
            public byte X;
            public byte Y;
            public byte Rotation;
        }

        private sealed class PlanScore : IComparable<PlanScore>
        {
            private readonly int groupPenalty;
            private readonly int occupiedPages;
            private readonly int highestUsedPage;
            private readonly int extent;

            public PlanScore(
                int groupPenalty,
                int occupiedPages,
                int highestUsedPage,
                int extent)
            {
                this.groupPenalty = groupPenalty;
                this.occupiedPages = occupiedPages;
                this.highestUsedPage = highestUsedPage;
                this.extent = extent;
            }

            public int CompareTo(PlanScore other)
            {
                int result = groupPenalty.CompareTo(other.groupPenalty);
                if (result == 0)
                {
                    result = occupiedPages.CompareTo(other.occupiedPages);
                }
                if (result == 0)
                {
                    result = highestUsedPage.CompareTo(other.highestUsedPage);
                }
                if (result == 0)
                {
                    result = extent.CompareTo(other.extent);
                }
                return result;
            }
        }

        private sealed class Bounds
        {
            public int MinX;
            public int MinY;
            public int MaxX;
            public int MaxY;
            public int CellArea;
        }
    }
}
