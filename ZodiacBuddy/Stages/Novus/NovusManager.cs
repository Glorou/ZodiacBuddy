﻿using System;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Gui.Toast;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ZodiacBuddy.BonusLight;

namespace ZodiacBuddy.Stages.Novus;

/// <summary>
/// Your buddy for the Novus stage.
/// </summary>
internal class NovusManager : IDisposable {
    private static readonly BonusLightLevel[] BonusLightValues = {
        #pragma warning disable format,SA1008,SA1025
        new(  8, 4649), // Feeble
        new( 16, 4650), // Gentle
        new( 32, 4651), // Bright
        new( 48, 4652), // Brilliant
        new( 96, 4653), // Blinding
        new(128, 4654), // Newborn Star
        #pragma warning restore format,SA1008,SA1025
    };
    
    private readonly NovusWindow window;

    private DateTime? dutyBeginning;
    private bool onDutyFromBeginning;

    /// <summary>
    /// Initializes a new instance of the <see cref="NovusManager"/> class.
    /// </summary>
    public NovusManager() {
        this.window = new NovusWindow();

        Service.Framework.Update += this.OnUpdate;
        Service.Toasts.QuestToast += this.OnToast;
        Service.Interface.UiBuilder.Draw += this.window.Draw;
        Service.ClientState.TerritoryChanged += this.OnTerritoryChange;
        Service.DutyState.DutyStarted += this.OnDutyStart;

        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "RelicGlass", AddonRelicGlassOnSetupDetour);
    }

    private static NovusConfiguration Configuration => Service.Configuration.Novus;

    /// <inheritdoc/>
    public void Dispose() {
        Service.Framework.Update -= this.OnUpdate;
        Service.Interface.UiBuilder.Draw -= this.window.Draw;
        Service.Toasts.QuestToast -= this.OnToast;
        Service.ClientState.TerritoryChanged -= this.OnTerritoryChange;
        Service.DutyState.DutyStarted -= this.OnDutyStart;

        Service.AddonLifecycle.UnregisterListener(AddonRelicGlassOnSetupDetour);
    }

    private void AddonRelicGlassOnSetupDetour(AddonEvent type, AddonArgs args) {
        this.UpdateRelicGlassAddon(0, 4u);
        this.UpdateRelicGlassAddon(1, 5u);
    }

    private unsafe void UpdateRelicGlassAddon(int slot, uint nodeId) {
        var item = Util.GetEquippedItem(slot);
        if (!NovusRelic.Items.ContainsKey(item.ItemId))
            return;

        var addon = (AtkUnitBase*)Service.GameGui.GetAddonByName("RelicGlass");
        if (addon == null)
            return;

        var componentNode = (AtkComponentNode*)addon->UldManager.SearchNodeById(nodeId);
        if (componentNode == null)
            return;

        var lightText = (AtkTextNode*)componentNode->Component->UldManager.SearchNodeById(8);
        if (lightText == null)
            return;

        if (Configuration.ShowNumbersInRelicGlass) {
            var value = item.SpiritbondOrCollectability;
            lightText->SetText($"{lightText->NodeText} {value}/2000");
        }

        if (!Configuration.DontPlayRelicGlassAnimation)
            return;

        var analyzeText = (AtkTextNode*)componentNode->Component->UldManager.SearchNodeById(7);
        if (analyzeText == null)
            return;

        analyzeText->SetText(lightText->NodeText.ToString());
    }

    private void OnUpdate(IFramework framework) {
        try {
            if (!Configuration.DisplayRelicInfo) {
                this.window.ShowWindow = false;
                return;
            }

            var mainhand = Util.GetEquippedItem(0);
            var offhand = Util.GetEquippedItem(1);

            var shouldShowWindow =
                NovusRelic.Items.ContainsKey(mainhand.ItemId) ||
                NovusRelic.Items.ContainsKey(offhand.ItemId);

            this.window.ShowWindow = shouldShowWindow;
            this.window.MainHandItem = mainhand;
            this.window.OffhandItem = offhand;
        }
        catch (Exception ex) {
            Service.PluginLog.Error(ex, $"Unhandled error during {nameof(NovusManager)}.{nameof(this.OnUpdate)}");
        }
    }

    private void OnToast(ref SeString message, ref QuestToastOptions options, ref bool isHandled) {
        try {
            this.OnToastInner(ref message, ref options, ref isHandled);
        }
        catch (Exception ex) {
            Service.PluginLog.Error(ex, $"Unhandled error during {nameof(NovusManager)}.{nameof(this.OnToast)}");
        }
    }

    private void OnToastInner(ref SeString message, ref QuestToastOptions _, ref bool isHandled) {
        if (isHandled)
            return;

        // Avoid double display if mainhand AND offhand is equipped
        if (NovusRelic.Items.ContainsKey(Util.GetEquippedItem(0).ItemId) &&
            NovusRelic.Items.TryGetValue(Util.GetEquippedItem(1).ItemId, out var relicName) &&
            message.ToString().Contains(relicName))
            return;

        foreach (var lightLevel in BonusLightValues) {
            if (!message.ToString().Contains(lightLevel.Message))
                continue;

            Service.Plugin.PrintMessage($"Light Intensity has increased by {lightLevel.Intensity}.");

            var territoryId = Service.ClientState.TerritoryType;
            if (!BonusLightDuty.TryGetValue(territoryId, out var territoryLight))
                return;

            if (territoryLight == null || lightLevel.Intensity <= territoryLight.DefaultLightIntensity)
                return;

            Service.BonusLightManager.AddLightBonus(territoryId, this.dutyBeginning, this.onDutyFromBeginning, $"Light bonus detected on \"{territoryLight.DutyName}\"");
            return;
        }
    }

    private void OnTerritoryChange(ushort territoryId) {
        // Reset territory info
        this.dutyBeginning = null;
        this.onDutyFromBeginning = false;

        if (!BonusLightDuty.TryGetValue(territoryId, out _))
            return;

        this.dutyBeginning = DateTime.UtcNow;
    }

    private void OnDutyStart(object? sender, ushort territoryId) {
        // Prevent report from player reconnecting during duty or joining an ongoing duty
        // Can set dutyBeginning due to player in cinematic
        this.onDutyFromBeginning = true;
    }
}
