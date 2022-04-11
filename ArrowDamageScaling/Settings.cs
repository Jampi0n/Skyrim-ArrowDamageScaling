using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Mutagen.Bethesda.Synthesis.Settings;

namespace ArrowDamageScaling {

    public class EmulateActorValueEntryPoints {
        [SynthesisTooltip("Uses multiple perk entry points to emulate actor value based entry points. If disabled, actor value based entry points will be skipped and arrow damage will not be modified by them. Recommended.")]
        public bool Enabled = true;

        [SynthesisTooltip("Actor value range that is emulated by a single perk entry point. Lower values mean more Accuracy, but require more perk entry points. The number of required entry points is MaximumActorValue/Accuracy.")]
        public int Accuracy = 10;

        [SynthesisTooltip("Actor value scaling up to this maximum is emulated. In vanilla Skyrim, 160 is a reasonable value, because the skill modifiers (Alchemy and Enchantments) will rarely exceed 160%.")]
        public int MaximumActorValue = 160;

        [SynthesisTooltip("Increases arrow damage based on Marksman skill level.")]
        public float SkillScaling = 0.005f;
    }

    public class Balancing {
        [SynthesisTooltip("Additive modifier for arrow damage applied before all other modifiers.")]
        public float arrowDamageOffset = 0f;
        [SynthesisTooltip("Multiplicative modifier for arrow damage.")]
        public float arrowDamageFactor = 1f;
        [SynthesisTooltip("Additive modifier for bow damage applied before all other modifiers.")]
        public float bowDamageOffset = 0f;
        [SynthesisTooltip("Multiplicative modifier for bow damage.")]
        public float bowDamageFactor = 1f;
    }

    public class Settings {
        [SynthesisTooltip("Arrow damage is only modified for the player.")]
        public bool PlayerOnly = true;

        [SynthesisTooltip("How much arrow damage is scaled relative to bow damage. 1.0 means same scaling. 0.5 means, if bow damage is multiplied by 4, arrow damage is only multiplied by 2.")]
        public float ScalingFactor = 1.0f;

        public EmulateActorValueEntryPoints emulateActorValueEntryPoints = new();

        [SynthesisTooltip("Adjust arrow damage.")]
        public Balancing balancing = new();
    }
}
