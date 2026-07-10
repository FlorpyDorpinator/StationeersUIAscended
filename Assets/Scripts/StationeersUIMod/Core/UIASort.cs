using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Clothing;
using Assets.Scripts.Objects.Items;

namespace StationeersUIMod.Core
{
    /// <summary>
    /// The mod's own item taxonomy. The game's SortingClass is too coarse for radial
    /// grouping (11 classes, most items land in Default) — a bag of batteries and a bag of
    /// nested backpacks both read "Default"/"Clothing". These 22 classes are what the
    /// Option A bag radials group by: nested backpacks are Storage, cells are PowerCell.
    /// </summary>
    public enum UIAClass
    {
        Storage,
        Tool,
        Device,
        PowerCell,
        GasCanister,
        Tank,
        Filter,
        Ore,
        Ingot,
        Material,
        Kit,
        Electronics,
        Food,
        Ingredient,
        Seed,
        Medical,
        Clothing,
        SuitPart,
        Weapon,
        Consumable,
        Decor,
        Misc,
    }

    /// <summary>
    /// Classifier: generated per-prefab table first (built from the game's Stationpedia
    /// export, 785 vanilla items — see UIASortingData.g.cs), then class-based fallbacks so
    /// modded/unknown items land somewhere sane. People can ship their own tables later;
    /// this is the default set.
    /// </summary>
    public static class UIASort
    {
        public static UIAClass Classify(DynamicThing thing)
        {
            if (thing == null) return UIAClass.Misc;
            UIAClass c;
            if (UIASortingData.ByPrefabHash.TryGetValue(thing.PrefabHash, out c)) return c;
            return Fallback(thing);
        }

        private static UIAClass Fallback(DynamicThing t)
        {
            try
            {
                if (t is BatteryCell) return UIAClass.PowerCell;
                if (t is GasFilter) return UIAClass.Filter;
                if (t is GasCanister) return UIAClass.GasCanister;
                if (t is SuitBase || t is Jetpack) return UIAClass.SuitPart;
                if (t is Stackable) return UIAClass.Material;
                if (t is PowerTool || t is Tool) return UIAClass.Tool;

                switch (t.SortingClass)
                {
                    case SortingClass.Kits: return UIAClass.Kit;
                    case SortingClass.Tools: return UIAClass.Tool;
                    case SortingClass.Food: return UIAClass.Food;
                    case SortingClass.Clothing: return UIAClass.Clothing;
                    case SortingClass.Ores: return UIAClass.Ore;
                    case SortingClass.Ices: return UIAClass.Ore;
                    case SortingClass.Resources: return UIAClass.Material;
                    case SortingClass.Storage: return UIAClass.Storage;
                    case SortingClass.Atmospherics: return UIAClass.Tank;
                    case SortingClass.Appliances: return UIAClass.Device;
                }
                if (Features.ItemMenuBuilder.LooksLikeContainer(t)) return UIAClass.Storage;
            }
            catch { }
            return UIAClass.Misc;
        }

        /// <summary>Wedge label for a class group.</summary>
        public static string DisplayName(UIAClass c)
        {
            switch (c)
            {
                case UIAClass.Storage: return "Storage";
                case UIAClass.Tool: return "Tools";
                case UIAClass.Device: return "Devices";
                case UIAClass.PowerCell: return "Power Cells";
                case UIAClass.GasCanister: return "Canisters";
                case UIAClass.Tank: return "Tanks";
                case UIAClass.Filter: return "Filters";
                case UIAClass.Ore: return "Ores";
                case UIAClass.Ingot: return "Ingots";
                case UIAClass.Material: return "Materials";
                case UIAClass.Kit: return "Kits";
                case UIAClass.Electronics: return "Electronics";
                case UIAClass.Food: return "Food";
                case UIAClass.Ingredient: return "Ingredients";
                case UIAClass.Seed: return "Seeds";
                case UIAClass.Medical: return "Medical";
                case UIAClass.Clothing: return "Clothing";
                case UIAClass.SuitPart: return "Suit Parts";
                case UIAClass.Weapon: return "Weapons";
                case UIAClass.Consumable: return "Consumables";
                case UIAClass.Decor: return "Decor";
                default: return "Other";
            }
        }
    }
}
