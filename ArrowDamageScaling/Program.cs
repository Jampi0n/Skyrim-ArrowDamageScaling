using System;
using System.Collections.Generic;
using System.Linq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using System.Threading.Tasks;
using Mutagen.Bethesda.FormKeys.SkyrimLE;
using Mutagen.Bethesda.Plugins;

namespace ArrowDamageScaling {
    public static class InvertPerk {


        public static float infinity = 0x10000;
        public static float zero = 1f / infinity;

        public static bool IsInfinity(double? x) {
            return Math.Abs(x.GetValueOrDefault(0)) >= infinity;
        }
        public static bool IsZero(double? x) {
            return Math.Abs(x.GetValueOrDefault(0)) <= zero;
        }
        public static float EnsureBounds(double? x) {
            double tmp = x.GetValueOrDefault(0);
            if(IsInfinity(tmp)) {
                return Math.Sign(tmp) * infinity;
            }
            if(IsZero(tmp)) {
                if(Math.Sign(tmp) == 0) {
                    return zero;
                } else {
                    return Math.Sign(tmp) * zero;
                }
            }
            return (float) tmp;
        }

        /// <summary>
        /// Returns an actor value free copy of an actor value perk entry point.
        /// </summary>
        /// <param name="modifyActorValue">The actor value perk entry point.</param>
        /// <returns>The actor value free perk entry point.</returns>
        public static PerkEntryPointModifyValue GetModifyValueEntryPoint(PerkEntryPointModifyActorValue modifyActorValue) {
            modifyActorValue = modifyActorValue.DeepCopy();
            var modifyValue = new PerkEntryPointModifyValue() {
                Conditions = modifyActorValue.Conditions,
                EntryPoint = modifyActorValue.EntryPoint,

                PerkConditionTabCount = modifyActorValue.PerkConditionTabCount,
                Priority = modifyActorValue.Priority,
                Rank = modifyActorValue.Rank,


                Modification = PerkEntryPointModifyValue.ModificationType.Set,
                Value = 0f,
            };

            return modifyValue;
        }

        /// <summary>
        /// Adds actor value conditions (x <= AV < y), so that the perk entry point only applies if the owner's actor value is within a certain range.
        /// </summary>
        /// <param name="entryPoint">The perk entry point to which the conditions are added.</param>
        /// <param name="actorValue">The actor value that is used in the conditions.</param>
        /// <param name="min">Lower bound for the actor value. int.MinValue means no lower bound.</param>
        /// <param name="max">Upper bound for the actor value. int.MaxValue means no upper bound.</param>
        public static void AddActorValueConditions(APerkEntryPointEffect entryPoint, ActorValue actorValue, int min, int max) {
            PerkCondition? perkCond = entryPoint.Conditions.Find((PerkCondition cond) => {
                return cond.RunOnTabIndex == 0;
            });

            if(perkCond == null) {
                perkCond = new PerkCondition() {
                    RunOnTabIndex = 0
                };
                entryPoint.Conditions.Add(perkCond);
            }

            if(min != int.MinValue) {
                perkCond.Conditions.Insert(0, new ConditionFloat() {
                    CompareOperator = CompareOperator.GreaterThanOrEqualTo,
                    ComparisonValue = min - 0.5f,
                    Data = new FunctionConditionData() {
                        Function = Condition.Function.GetActorValue,
                        ParameterOneNumber = (int)actorValue,
                        RunOnType = Condition.RunOnType.Subject
                    }
                });
            }

            if(max != int.MaxValue) {
                perkCond.Conditions.Insert(0, new ConditionFloat() {
                    CompareOperator = CompareOperator.LessThan,
                    ComparisonValue = max - 0.5f,
                    Data = new FunctionConditionData() {
                        Function = Condition.Function.GetActorValue,
                        ParameterOneNumber = (int)actorValue,
                        RunOnType = Condition.RunOnType.Subject
                    }
                });
            }
        }

        /// <summary>
        /// Adjusts the perk entry points scaleAllEffect and scaleNonArrow, so they correctly modify only arrow damage. The resulting perk entry points ar eadded to addedEffects.
        /// </summary>
        /// <param name="addedEffects">An empty list that is used to hold additional return values. Entry points in this list must be added to the perk.</param>
        /// <param name="scaleAll">The perk entry point that scales all weapon damage.</param>
        /// <param name="scaleNonArrow">The perk entry point that scales all non arrow damage.</param>
        /// <returns>Returns true, if adjustint was succesful.</returns>
        public static bool Invert(List<APerkEffect> addedEffects, APerkEntryPointEffect scaleAll, APerkEntryPointEffect scaleNonArrow) {
            if(scaleNonArrow is PerkEntryPointModifyValue scaleNonArrowModifyValue && scaleAll is PerkEntryPointModifyValue scaleAllModifyValue) {
                switch(scaleNonArrowModifyValue.Modification) {
                    case PerkEntryPointModifyValue.ModificationType.Add: {
                        if(IsZero(scaleNonArrowModifyValue.Value)) {
                            // There is no damage modification
                            return false;
                        }
                        scaleAllModifyValue.Value *= Program.settings.ScalingFactor;
                        scaleNonArrowModifyValue.Value = -scaleAllModifyValue.Value;
                        addedEffects.Add(scaleAll);
                        addedEffects.Add(scaleNonArrow);
                        return true;
                    }
                    case PerkEntryPointModifyValue.ModificationType.Multiply: {
                        scaleAllModifyValue.Value = EnsureBounds(Math.Pow(scaleAllModifyValue.Value.GetValueOrDefault(0), Program.settings.ScalingFactor));
                        scaleNonArrowModifyValue.Value = EnsureBounds(1f / scaleNonArrowModifyValue.Value);
                        
                        addedEffects.Add(scaleAll);
                        addedEffects.Add(scaleNonArrow);
                        return true;
                    }
                    default: {
                        return false;
                    }
                }
            }
            bool emulate = Program.settings.emulateActorValueEntryPoints.Enabled;
            int accuracy = Program.settings.emulateActorValueEntryPoints.Accuracy;
            int maximum = Program.settings.emulateActorValueEntryPoints.MaximumActorValue;
            int numEntryPoints = maximum / accuracy + 1;
            var thresholds = new int[numEntryPoints + 1];
            var actorValues = new int[numEntryPoints + 1];
            actorValues[0] = 0;
            thresholds[0] = int.MinValue;
            thresholds[numEntryPoints] = int.MaxValue;
            actorValues[numEntryPoints] = maximum;
            for(int i = 1; i < numEntryPoints; ++i) {
                thresholds[i] = (int)(maximum / (numEntryPoints - 1f) * i);
                actorValues[i] = thresholds[i];
            }
            if(scaleNonArrow is PerkEntryPointModifyActorValue scaleNonArrowModifyActorValue && scaleAll is PerkEntryPointModifyActorValue scaleAllModifyActorValue) {
                switch(scaleNonArrowModifyActorValue.Modification) {
                    case PerkEntryPointModifyActorValue.ModificationType.AddAVMult: {
                        if(IsZero(scaleNonArrowModifyActorValue.Value)) {
                            // There is no damage modification
                            return false;
                        }
                        scaleAllModifyActorValue.Value *= Program.settings.ScalingFactor;
                        scaleNonArrowModifyActorValue.Value = -scaleAllModifyActorValue.Value;

                        addedEffects.Add(scaleAll);
                        addedEffects.Add(scaleNonArrow);
                        return true;
                    }
                    case PerkEntryPointModifyActorValue.ModificationType.MultiplyAVMult: {
                        // emulate
                        if(emulate) {
                            var scaleAllList = new PerkEntryPointModifyValue[numEntryPoints];
                            var scaleNonArrowList = new PerkEntryPointModifyValue[numEntryPoints];

                            for(int i = 0; i < numEntryPoints; ++i) {
                                scaleAllList[i] = GetModifyValueEntryPoint((PerkEntryPointModifyActorValue)scaleAll);
                                scaleNonArrowList[i] = GetModifyValueEntryPoint(scaleNonArrowModifyActorValue);
                                AddActorValueConditions(scaleAllList[i], scaleNonArrowModifyActorValue.ActorValue, thresholds[i], thresholds[i + 1]);
                                AddActorValueConditions(scaleNonArrowList[i], scaleNonArrowModifyActorValue.ActorValue, thresholds[i], thresholds[i + 1]);
                                scaleAllList[i].Modification = PerkEntryPointModifyValue.ModificationType.Multiply;
                                scaleAllList[i].Value = EnsureBounds(Math.Pow(actorValues[i] * scaleNonArrowModifyActorValue.Value, Program.settings.ScalingFactor));
                                scaleNonArrowList[i].Modification = PerkEntryPointModifyValue.ModificationType.Multiply;
                                scaleNonArrowList[i].Value = EnsureBounds(1f / scaleAllList[i].Value);
                                if(!IsZero(scaleAllList[i].Value - 1)) {
                                    addedEffects.Add(scaleAllList[i]);
                                }
                                if(!IsZero(scaleNonArrowList[i].Value - 1)) {
                                    addedEffects.Add(scaleNonArrowList[i]);
                                }
                            }
                            return true;
                        } else {
                            return false;
                        }

                    }
                    case PerkEntryPointModifyActorValue.ModificationType.MultiplyOnePlusAVMult: {
                        if(IsZero(scaleNonArrowModifyActorValue.Value)) {
                            // There is no damage modification
                            return false;
                        }
                        // emulate
                        if(emulate) {
                            var scaleAllList = new PerkEntryPointModifyValue[numEntryPoints];
                            var scaleNonArrowList = new PerkEntryPointModifyValue[numEntryPoints];

                            for(int i = 0; i < numEntryPoints; ++i) {
                                scaleAllList[i] = GetModifyValueEntryPoint((PerkEntryPointModifyActorValue)scaleAll);
                                scaleNonArrowList[i] = GetModifyValueEntryPoint(scaleNonArrowModifyActorValue);
                                AddActorValueConditions(scaleAllList[i], scaleNonArrowModifyActorValue.ActorValue, thresholds[i], thresholds[i + 1]);
                                AddActorValueConditions(scaleNonArrowList[i], scaleNonArrowModifyActorValue.ActorValue, thresholds[i], thresholds[i + 1]);
                                scaleAllList[i].Modification = PerkEntryPointModifyValue.ModificationType.Multiply;
                                scaleAllList[i].Value = EnsureBounds(Math.Pow(1 + actorValues[i] * scaleNonArrowModifyActorValue.Value, Program.settings.ScalingFactor));
                                scaleNonArrowList[i].Modification = PerkEntryPointModifyValue.ModificationType.Multiply;
                                scaleNonArrowList[i].Value = EnsureBounds(1f / scaleAllList[i].Value);
                                if(!IsZero(scaleAllList[i].Value - 1)) {
                                    addedEffects.Add(scaleAllList[i]);
                                }
                                if(!IsZero(scaleNonArrowList[i].Value - 1)) {
                                    addedEffects.Add(scaleNonArrowList[i]);
                                }
                            }
                            return true;
                        } else {
                            return false;
                        }
                    }
                    default: {
                        return false;
                    }
                }
            }
            return false;
        }
    }

    public class Program {

        public static Lazy<Settings> _settings = null!;
        public static Settings settings => _settings.Value;

        public static async Task<int> Main(string[] args) {
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetAutogeneratedSettings(
                    nickname: "Settings",
                    path: "settings.json",
                    out _settings)
                .SetTypicalOpen(GameRelease.SkyrimSE, "YourPatcher.esp")
                .Run(args);
        }

        /// <summary>
        /// Returns <see langword="true"/>, if this Weapon tab condition returns true in all situation for all bows.
        /// </summary>
        /// <param name="condition">The condition which is checked.</param>
        /// <returns>Whether this Weapon tab condition returns true in all situation for all bows.</returns>
        public static bool AffectsBowDamage(Condition condition) {
            // Only the HasKeyword condition in combination with the WeapTypeBow keyword is supported.
            if(condition is ConditionFloat conditionFloat) {
                if(conditionFloat.Data is FunctionConditionData functionConditionData) {
                    if(functionConditionData.Function == Condition.Function.HasKeyword && functionConditionData.RunOnType == Condition.RunOnType.Subject) {
                        if(functionConditionData.ParameterOneRecord.FormKey == Skyrim.Keyword.WeapTypeBow.FormKey) {
                            if(conditionFloat.CompareOperator == CompareOperator.EqualTo && conditionFloat.ComparisonValue == 1f) {
                                return true;
                            }
                            if(conditionFloat.CompareOperator == CompareOperator.NotEqualTo && conditionFloat.ComparisonValue == 0f) {
                                return true;
                            }
                        } else {
                            if(conditionFloat.CompareOperator == CompareOperator.EqualTo && conditionFloat.ComparisonValue == 0f) {
                                return true;
                            }
                            if(conditionFloat.CompareOperator == CompareOperator.NotEqualTo && conditionFloat.ComparisonValue == 1f) {
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Returns <see langword="true"/>, if the sequence of Weapon tab conditions returns true in all situation for all bows.
        /// </summary>
        /// <param name="conditions">The sequence of Weapon tab conditions which is checked.</param>
        /// <returns>Whether the sequence of Weapon tab conditions returns true in all situation for all bows.</returns>
        public static bool AffectsBowDamage(List<Condition> conditions) {
            // An empty list always returns true.
            if(conditions.Count == 0) {
                return true;
            }


            // Consecutive ORs are treated like a single block when evaluating and have order precedence over AND.
            // First a list of OR-blocks is built.
            List<List<Condition>> orBlocks = new();
            orBlocks.Add(new List<Condition>());
            foreach(var cond in conditions) {
                orBlocks.First().Add(cond);
                if(!cond.Flags.HasFlag(Condition.Flag.OR)) {
                    orBlocks.Insert(0, new List<Condition>());
                }
            }

            // Evaluate list of OR-blocks.
            // Results of OR-blocks are connected with AND.
            bool eval = true;
            foreach(var orBlock in orBlocks) {
                bool blockEval = false;
                if(orBlock.Count == 0) {
                    blockEval = true;
                }
                foreach(var orCond in orBlock) {
                    if(AffectsBowDamage(orCond)) {
                        blockEval = true;
                        break;
                    }
                }
                eval = eval && blockEval;
            }

            return eval;
        }

        /// <summary>
        /// Returns <see langword="true"/>, if the Weapon condition tab of <paramref name="entryPoint"/> returns true in all situations for all bows.
        /// </summary>
        /// <param name="entryPoint">The perk entry point for which the conditions are checked.</param>
        /// <returns>Whether the Weapon condition tab of <paramref name="entryPoint"/> returns true in all situation for all bows.</returns>
        public static bool AffectsBowDamage(APerkEntryPointEffect entryPoint) {
            // The relevant entry points have three condition tabs: (0=Perk Owner, 1=Weapon, 2=Target)
            foreach(var perkCond in entryPoint.Conditions) {
                if(perkCond.RunOnTabIndex == 1) {
                    return AffectsBowDamage(perkCond.Conditions);
                }
            }
            return true;
        }

        /// <summary>
        /// Removes all Weapon conditions from the perk entry point.
        /// </summary>
        /// <param name="entryPoint">The perk entry point from which all Weapon conditions are removed.</param>
        public static void RemoveWeaponConditions(APerkEntryPointEffect entryPoint) {
            // The relevant entry points have three condition tabs: (0=Perk Owner, 1=Weapon, 2=Target)
            entryPoint.Conditions.RemoveAll((PerkCondition cond) => {
                return cond.RunOnTabIndex == 1;
            });
        }

        /// <summary>
        /// Adds a condition to the perk entry point so it only affects the player.
        /// </summary>
        /// <param name="entryPoint">The perk entry point to which the condition is added.</param>
        public static void AddPlayerCondition(APerkEntryPointEffect entryPoint) {
            // The relevant entry points have three condition tabs: (0=Perk Owner, 1=Weapon, 2=Target)

            // Create condition for Perk Owner tab if necessary.
            PerkCondition? perkCond = entryPoint.Conditions.Find((PerkCondition cond) => {
                return cond.RunOnTabIndex == 0;
            });
            if(perkCond == null) {
                perkCond = new PerkCondition() {
                    RunOnTabIndex = 0
                };
                entryPoint.Conditions.Add(perkCond);
            }

            perkCond.Conditions.Insert(0, new ConditionFloat() {
                CompareOperator = CompareOperator.EqualTo,
                ComparisonValue = 1f,
                Data = new FunctionConditionData() {
                    Function = Condition.Function.HasKeyword,
                    ParameterOneRecord = Skyrim.Keyword.PlayerKeyword,
                    RunOnType = Condition.RunOnType.Subject
                }
            });
        }

        /// <summary>
        /// Adds a condition to the perk entry point so it only affects non-arrows.
        /// </summary>
        /// <param name="entryPoint">The perk entry point to which the condition is added.</param>
        public static void AddNonArrowConditions(APerkEntryPointEffect entryPoint) {
            // The relevant entry points have three condition tabs: (0=Perk Owner, 1=Weapon, 2=Target)

            // Create condition for Weapon tab if necessary.
            PerkCondition? perkCond = entryPoint.Conditions.Find((PerkCondition cond) => {
                return cond.RunOnTabIndex == 1;
            });
            if(perkCond == null) {
                perkCond = new PerkCondition() {
                    RunOnTabIndex = 1
                };
                entryPoint.Conditions.Add(perkCond);
            }

            // Add trivial condition:
            // HasKeyword(X) == 0 || HasKeyword(X) != 0
            // This condition should always be true. However arrows are not weapon records, so having these conditions for arrows actually results in the conditions being false.
            // As a result, the perk entry point is applied to everything but arrows.
            // Note: The conditions are still true for unarmed attacks.
            perkCond.Conditions.Add(new ConditionFloat() {
                CompareOperator = CompareOperator.EqualTo,
                ComparisonValue = 0f,
                Flags = Condition.Flag.OR,
                Data = new FunctionConditionData() {
                    Function = Condition.Function.HasKeyword,
                    ParameterOneRecord = Skyrim.Keyword.ActivatorLever,
                    RunOnType = Condition.RunOnType.Subject
                }
            });
            perkCond.Conditions.Add(new ConditionFloat() {
                CompareOperator = CompareOperator.NotEqualTo,
                ComparisonValue = 0f,
                Data = new FunctionConditionData() {
                    Function = Condition.Function.HasKeyword,
                    ParameterOneRecord = Skyrim.Keyword.ActivatorLever,
                    RunOnType = Condition.RunOnType.Subject
                }
            });
        }

        static FormLink<IPerkGetter> playerPerk = Skyrim.Perk.AllowShoutingPerk;

        static List<FormLink<IPerkGetter>> playerOnlyPerks = new() {
            Skyrim.Perk.AlchemySkillBoosts.FormKey,
            Skyrim.Perk.PerkSkillBoosts.FormKey,
        };

        /// <summary>
        /// Gets the perk to which the changes are supposed to be applied.
        /// </summary>
        /// <param name="state">The Synthesis patcher state.</param>
        /// <param name="perkGetter">The perk which needs to be patched.</param>
        /// <returns></returns>
        public static Perk GetPatchPerk(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, IPerkGetter perkGetter) {
            if(settings.PlayerOnly && playerOnlyPerks.Contains(perkGetter.FormKey)) {
                // If PlayerOnly is enabled, perk effects from SkillBoosts (Alchemy and Enchanting) are moved to another player only perk
                // This is done, because there are mods that give NPCs the SkillBoosts perks and having many additional entry points may affect performance
                return state.PatchMod.Perks.GetOrAddAsOverride(playerPerk.Resolve(state.LinkCache));
            }
            return state.PatchMod.Perks.GetOrAddAsOverride(perkGetter);
        }

        /// <summary>
        /// Patches a perk.
        /// </summary>
        /// <param name="state">The Synthesis patcher state.</param>
        /// <param name="perkGetter">The perk which needs to be patched.</param>
        public static void PatchPerk(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, IPerkGetter perkGetter) {
            List<APerkEntryPointEffect> relevantEffects = new();
            // Loop through entry points.
            foreach(var effect in perkGetter.Effects) {
                if(effect is APerkEntryPointEffect entryPointEffect) {
                    // Only consider damage modifying entry points.
                    if(entryPointEffect.EntryPoint == APerkEntryPointEffect.EntryType.CalculateWeaponDamage || entryPointEffect.EntryPoint == APerkEntryPointEffect.EntryType.ModAttackDamage) {
                        // Check if the entry point affects bows.
                        if(AffectsBowDamage(entryPointEffect)) {
                            relevantEffects.Add(entryPointEffect);
                        }
                    }
                }
            }
            if(relevantEffects.Count > 0) {
                Perk? perk = null;
                foreach(var effect in relevantEffects) {
                    // Copy entry point twice, including conditions and priority.
                    var scaleAllEffect = effect.DeepCopy();
                    var scaleNonArrow = effect.DeepCopy();

                    // Adjust conditions of copied entry points.
                    if(settings.PlayerOnly) {
                        AddPlayerCondition(scaleAllEffect);
                        AddPlayerCondition(scaleNonArrow);
                    }
                    RemoveWeaponConditions(scaleAllEffect);
                    RemoveWeaponConditions(scaleNonArrow);
                    AddNonArrowConditions(scaleNonArrow);

                    var addedEffects = new List<APerkEffect>();
                    // Adjust entry points if supported.
                    // Usually this means the non arrow entry point is inverted.
                    // For actor value based entry points, both need to be emulated using multiple entry points.
                    // The addedEffects list is used to give the Invert function the option to add additional entry points required for emulation.
                    // Returns true, if the entry points are supported. The addedEffects list will contain all entry points that need to be added.
                    if(InvertPerk.Invert(addedEffects, scaleAllEffect, scaleNonArrow)) {
                        if(perk == null) {
                            perk = GetPatchPerk(state, perkGetter);
                        }
                        foreach(var addEffect in addedEffects) {
                            perk.Effects.Add(addEffect);
                        }
                    }
                }
            }
        }

        public static List<string> CheckSettings() {
            var errorList = new List<string>();
            if(settings.ScalingFactor < 0) {
                errorList.Add("ScalingFactor must not be negative.");
            }
            if(settings.emulateActorValueEntryPoints.Enabled) {
                if(settings.emulateActorValueEntryPoints.Accuracy < 1) {
                    errorList.Add("Accuracy must be at least 1.");
                }
                if(settings.emulateActorValueEntryPoints.MaximumActorValue < settings.emulateActorValueEntryPoints.Accuracy) {
                    errorList.Add("Accuracy cannot be larger than MaximumActorValue");
                }
                if(settings.emulateActorValueEntryPoints.SkillScaling < 0) {
                    errorList.Add("SkillScaling must not be negative.");
                }
            }
            return errorList;
        }

        /// <summary>
        /// Runs the patcher.
        /// </summary>
        /// <param name="state">The Synthesis patcher state.</param>
        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state) {
            var errors = CheckSettings();
            if(errors.Count > 0) {
                foreach(var error in errors) {
                    Console.WriteLine(error);
                }
                throw new ArgumentException("At least one of the provided settings was not valid.");
            }


            Perk? skillScalingPerk = null;
            List<APerkEntryPointEffect> dummyEffects = new();

            /*
             * Dummy perk effects are added to a player only perk before the main patcher runs and removed afterwards.
             * This means patcher sees these dummy perk effects and will add arrow scaling for these effects.
             * This is useful to add additional arrow only modifiers with little effort, as the logic to transform perk effecst to arrow only perk effects is already implemented by the main patcher.
             */

            /*
             * Skill scaling for arrow damage.
             * Skill scaling for bow damage is implemented with game settings, so it will not be applied to arrow damage by default.
             * A dummy perk entry with skill scaling for bow damage is added, so the main patcher adds skill scaling for arrow damage.
             */
            if(settings.emulateActorValueEntryPoints.SkillScaling != 0f) {
                dummyEffects.Add(new PerkEntryPointModifyActorValue() {
                    ActorValue = ActorValue.Archery,
                    EntryPoint = APerkEntryPointEffect.EntryType.ModAttackDamage,
                    Modification = PerkEntryPointModifyActorValue.ModificationType.MultiplyOnePlusAVMult,
                    Priority = 20,
                    Rank = 0,
                    PerkConditionTabCount = 3,
                    Value = settings.emulateActorValueEntryPoints.SkillScaling
                });
            }

            /*
             * Factor for arrow damage.
             */
            if(settings.balancing.arrowDamageFactor != 1f) {
                dummyEffects.Add(new PerkEntryPointModifyValue() {
                    EntryPoint = APerkEntryPointEffect.EntryType.ModAttackDamage,
                    Modification = PerkEntryPointModifyValue.ModificationType.Multiply,
                    Priority = 0,
                    Rank = 0,
                    PerkConditionTabCount = 3,
                    Value = settings.balancing.arrowDamageFactor
                });
            }

            /*
             * Initial offset for arrow damage.
             */
            if(settings.balancing.arrowDamageOffset != 0f) {
                dummyEffects.Add(new PerkEntryPointModifyValue() {
                    EntryPoint = APerkEntryPointEffect.EntryType.ModAttackDamage,
                    Modification = PerkEntryPointModifyValue.ModificationType.Add,
                    Priority = 255,
                    Rank = 0,
                    PerkConditionTabCount = 3,
                    Value = settings.balancing.arrowDamageOffset
                });
            }

            // Add dummy effects to player only perk.
            if(dummyEffects.Count > 0) {
                skillScalingPerk = state.PatchMod.Perks.GetOrAddAsOverride(playerPerk.Resolve(state.LinkCache));
                foreach(var effect in dummyEffects) {
                    skillScalingPerk!.Effects.Add(effect);
                }
            }

            // Run main patcher.
            if(settings.ScalingFactor != 0) {
                foreach(var perkGetter in state.LoadOrder.PriorityOrder.Perk().WinningOverrides()) {
                    PatchPerk(state, perkGetter);
                }
            }

            // Remove dummy effects from player only perk.
            foreach(var effect in dummyEffects) {
                skillScalingPerk!.Effects.Remove(effect);
            }

            /*
             * Factor for bow damage.
             */
            if(settings.balancing.bowDamageFactor != 1f) {
                var entryPoint = new PerkEntryPointModifyValue() {
                    EntryPoint = APerkEntryPointEffect.EntryType.ModAttackDamage,
                    Modification = PerkEntryPointModifyValue.ModificationType.Multiply,
                    Priority = 0,
                    Rank = 0,
                    PerkConditionTabCount = 3,
                    Value = settings.balancing.bowDamageFactor
                };
                if(settings.PlayerOnly) {
                    AddPlayerCondition(entryPoint);
                }
                entryPoint.Conditions.Add(new PerkCondition() {
                    RunOnTabIndex = 1,
                    Conditions = new Noggog.ExtendedList<Condition>() {
                        new ConditionFloat() {
                            CompareOperator = CompareOperator.EqualTo,
                            ComparisonValue = 1f,
                            Data = new FunctionConditionData() {
                                Function = Condition.Function.HasKeyword,
                                ParameterOneRecord = Skyrim.Keyword.WeapTypeBow,
                                RunOnType = Condition.RunOnType.Subject
                            }
                        }
                    }
                });
                skillScalingPerk = state.PatchMod.Perks.GetOrAddAsOverride(playerPerk.Resolve(state.LinkCache));
                skillScalingPerk.Effects.Add(entryPoint);
            }

            /*
             * Initial offset for bow damage.
             */
            if(settings.balancing.bowDamageOffset != 0f) {
                var entryPoint = new PerkEntryPointModifyValue() {
                    EntryPoint = APerkEntryPointEffect.EntryType.ModAttackDamage,
                    Modification = PerkEntryPointModifyValue.ModificationType.Add,
                    Priority = 255,
                    Rank = 0,
                    PerkConditionTabCount = 3,
                    Value = settings.balancing.bowDamageOffset
                };
                if(settings.PlayerOnly) {
                    AddPlayerCondition(entryPoint);
                }
                entryPoint.Conditions.Add(new PerkCondition() {
                    RunOnTabIndex = 1,
                    Conditions = new Noggog.ExtendedList<Condition>() {
                        new ConditionFloat() {
                            CompareOperator = CompareOperator.EqualTo,
                            ComparisonValue = 1f,
                            Data = new FunctionConditionData() {
                                Function = Condition.Function.HasKeyword,
                                ParameterOneRecord = Skyrim.Keyword.WeapTypeBow,
                                RunOnType = Condition.RunOnType.Subject
                            }
                        }
                    }
                });
                skillScalingPerk = state.PatchMod.Perks.GetOrAddAsOverride(playerPerk.Resolve(state.LinkCache));
                skillScalingPerk.Effects.Add(entryPoint);
            }
        }
    }
}
