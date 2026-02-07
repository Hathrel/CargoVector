using KeenSoftwareHouse.Library.Extensions;
using Sandbox.Game.AI.Pathfinding;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.Screens.Terminal.Controls;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.GameServices;
using VRageMath;
using VRageRender;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
// CARGO VECTOR 1.1
//
// CHANGELOG
//
// 1/4/2025 - v1.1 - Added helper methods to correct theoretical hover feasibility on 
// Pertram and Earthlike without actually needing to be in gravity.
//
// INTRODUCTION--------------------------------------------------------------------------
// This script was designed primarily for logistics ships to determine if they're over
// filled, but can be used for any ship to determine if it is capable of going up and
// down in the gravity well. 
//
// The panel portion is designed to be design friendly, so you
// can plan out how you want your screen to look in the Custom Data of the panels you
// use. This does necessarily mean that if another panel script doesn't just bowl over
// the rest of the text on the screen or nuke Custom Data, they can both occupy the same 
// screen, but this is rare.
//
// If you point it at an LCD/cockpit surface that another script also uses,
// expect conflicts, as I can't control the behavior of their script, but this one
// does not hog the entire surface.
//
//---------------------------------------------------------------------------------------

// USER CONFIG---------------------------------------------------------------------------
// This script is designed to be driven entirely by Custom Data.
//
// The goal: you can name blocks however you want; config lives in Custom Data.
// --------------------------------------------------------------------------------------

// --------------------------------------------------------------------------------------
// PB ARGUMENTS
// 1.) rescan - Type "rescan" in the arguments bar and click "Run" to immediately
// populate newly added screens. This doesn't HAVE to be done. The script default
// scans for new screens every ~30s.
// 2.) speed - Type "speed" in the arguments bar and click "Run" to immediately
// change the update speed to a new value. Run this if you've changed the
// updateSpeed value.
// --------------------------------------------------------------------------------------

// CUSTOMDATA ARGUMENTS------------------------------------------------------------------
// 
// Panel Arguments ----------------------------------------------------------------------
//
// TWR - [TWR]
// Displays current Thrust-to-Weight Ratio as a double.
// 
// • TWR > 1.00  → Ship can climb.
// • TWR = 1.00  → Neutral hover (no vertical authority).
// • TWR < 1.00  → Ship will descend even at full thrust.
//
// This value is instantaneous and reflects current mass, gravity, and available thrust.
//
// --------------------------------------------------------------------------------------
//
// ACCEL - [ACCEL]
// Displays NET vertical acceleration (m/s²) if full upward thrust is applied.
//
// • > 0  → You can arrest descent and climb.
// • = 0  → Neutral hover.
// • < 0  → You cannot stop falling; descent will accelerate.
//
// This is the *actual* braking capability metric.
//
// CANHOVER - [CANHOVER]
// Displays true/false indicating whether TWR ≥ 1.00.
//
// This is a convenience flag only and is NOT predictive.
// It does NOT indicate safe operation.
//
// STOPDIST - [STOPDIST]
// Displays estimated stopping distance (meters) required to null current downward
// velocity assuming full upward thrust.
//
// This value grows rapidly with speed and shrinking acceleration.
// Useful for intuition, not for envelope protection.
//
// BRAKEDIST - [BRAKEDIST]
// Displays braking feasibility relative to the current deck logic.
//
// Possible outputs include:
// • "BRAKE ALT: OK"
// • "BRAKE ALT: WARNING"
// • "BRAKE ALT: IMPOSSIBLE"
//
// This is a *diagnostic* display and should not be used for control decisions.
//
// ALTDIST - [ALTDIST]
// Displays altitude difference (meters) between current position and the relevant
// deck altitude.
//
// This is informational only and does not account for momentum or future authority loss.
//
// EARTHLIKE - [EARTHLIKE]
// Displays true/false indicating whether the ship can hover on an Earth-like planet
// (≈ 1.0g) at current mass and thrust.
//
// PERTRAM - [PERTRAM]
// Displays true/false indicating whether the ship can hover on Pertram
// (higher gravity than Earth-like).
//
// Light Arguments-----------------------------------------------------------------------
//
// ROTATING - [ROTATING]
// Marks a lighting block as a rotating / beacon-style light.
//
// Rotating lights are excluded from standard warning color logic and remain red unless
// explicitly overridden.
//
// --------------------------------------------------------------------------------------


public string controlledBlockIdentifier = "<Cargo Vector>";
// Name (or partial name) of the block this script controls.
// Brackets <> are recommended because they make the name easy to target and
// reduce accidental matches. If you remove brackets, be careful: this script
// does not enforce exact-name matching. Any ROW of text containing this identifier
// will be ignored, so make sure you put everything beneath this.

public string surfaceName = "Screen=";
// Prefix key used inside Custom Data to select which surface index to write to.
// Example: "[Screen=1]"

public double warningLightBlink = 2.0;
// During certain emergency states, lights controlled by this script will begin blinking.
// Set this to determine how rapidly they blink. The f is required, leave it.

public const int SOUND_DELAY = 180;
// Change this to increase or decrease the delay time between alarm sounds. Default is 180
// which plays at a rate of about 1 play every 3 seconds.

public string  alertSound = "Alert 1";
// This is the sound the sound block will play as an alarm in certain situations. Change
// this to change the sound that plays.

public bool playAlertSound = true;
// Default behavior of the alert soundblocks. 
// Set to false to play no alarm through soundblocks.

public char startArgumentsDelimiter = '[';
public char endArgumentsDelimiter = ']';
public char newArgumentDelimiter = ',';
// These characters define how dynamic arguments are embedded in display templates.
// Anything between startArgumentsDelimiter and endArgumentsDelimiter is treated as
// a command block, and multiple arguments inside that block are separated using
// newArgumentDelimiter.
//
// Example (using defaults):
//   "Speed: [ACCEL,TWR]"
//                 ^         ^       ^
//                 |           |        |
//                 |           |       +----- endArgumentsDelimiter (']')
//                 |          +--------- newArgumentDelimiter (',')
//                 +--------------- startArgumentsDelimiter ('[')
//
// In this example, "ACCEL" and "TWR" are parsed as two separate arguments from the
// same command block. Changing these characters lets you avoid conflicts with
// other scripts or formatting styles, but ALL templates must match the chosen
// delimiters exactly. If for any reason you choose \ it must be \\ or the code breaks.
// This is a code limitation, not a script limitation.

public double twrLimit = 2.00;
// User-defined operational TWR limit (comfort / policy threshold).

public int updateSpeed = 4;
// Update speed tier (NOT the raw UpdateFrequency enum):
// 1 = manual only (no autonomous updates; must be triggered externally)
// 2 = fast
// 3 = medium
// 4 = slow
// Anything else = run once, then stop unless triggered again

const int RESCAN_INTERVAL = 1800;
// How frequently the script scans for new screens.
// Space Engineers runs at 60 simulation ticks per second.
// 1800 ticks ≈ 30 simulation seconds at 1.0 sim speed.
//
// WARNING:
// Setting this to very low values will increase CPU load and may reduce sim
// speed, especially on grids with many LCDs or text surface providers.

//--------------------------DO NOT EDIT ANYTHING BELOW THIS LINE-------------------------

public Program()
{
    Runtime.UpdateFrequency = ParseFrequency();
}

private int rescanTimer = RESCAN_INTERVAL;

private List<SurfaceTarget> targetSurfaces = new List<SurfaceTarget>();
private Dictionary<int, IMyTextSurfaceProvider> providerScreens = new Dictionary<int, IMyTextSurfaceProvider>();

private List<IMyLightingBlock> controlledLights = new List<IMyLightingBlock>();
private List<IMySoundBlock> controlledSoundBlocks = new List<IMySoundBlock>();

public void Main(string argument)
{
    Echo(TickTimer());
    if (argument == "rescan")
        rescanTimer = RESCAN_INTERVAL;

    if (argument == "speed")
        Runtime.UpdateFrequency = ParseFrequency();

    Rescan();

    UpdateDisplays();
    ProcessLights();
    ProcessAlarms();
}

// Turns user input on the updateSpeed into an UpdateFrequency enum. Simplified
// from built-in numeric identifiers.
public UpdateFrequency ParseFrequency()
{
    switch (updateSpeed)
    {
        case 1: return UpdateFrequency.None;
        case 2: return UpdateFrequency.Update100;
        case 3: return UpdateFrequency.Update10;
        case 4: return UpdateFrequency.Update1;
        default: return UpdateFrequency.Once;
    }
}

//-----------------------------------DISPLAY HANDLING-----------------------------------
private struct SurfaceTarget
{
    public IMyTextSurface Surface;
    public IMyTerminalBlock Source;
}

// Processes the rescanTimer. Once it hits the interval, screens are cleared
// and reprocessed
public void Rescan()
{
    List<IMyTerminalBlock> terminaBlocks = new List<IMyTerminalBlock>();
    List<IMyTextSurfaceProvider> surfaceProviders = new List<IMyTextSurfaceProvider>();

    if (++rescanTimer >= RESCAN_INTERVAL)
    {
        rescanTimer = 0;

        targetSurfaces.Clear();
        providerScreens.Clear();
        surfaceProviders.Clear();
        controlledLights.Clear();
        controlledSoundBlocks.Clear();

        GridTerminalSystem.GetBlocksOfType(terminaBlocks);

        foreach (var p in terminaBlocks)
        {
            if (!p.CustomData.Contains(controlledBlockIdentifier))
                continue;

            if (p is IMyTextSurface)
            {
                // This surface IS the terminal block (LCD/etc.)
                targetSurfaces.Add(new SurfaceTarget
                {
                    Surface = (IMyTextSurface)p,
                    Source  = p
                });
            }
            else if (p is IMyLightingBlock)
                controlledLights.Add((IMyLightingBlock)p);
            else if (p is IMySoundBlock)
                controlledSoundBlocks.Add((IMySoundBlock)p);
            else if (p is IMyTextSurfaceProvider)
                surfaceProviders.Add((IMyTextSurfaceProvider)p);
        }

        foreach (IMyTextSurfaceProvider provider in surfaceProviders)
        {
            IMyTerminalBlock terminal = provider as IMyTerminalBlock;
            if (terminal == null) continue;

            List<string> tokens = GetArgumentTokens(terminal.CustomData);

            int index = tokens.FindIndex(x => x.StartsWith(surfaceName.ToUpperInvariant()));
            if (index < 0)
                continue;

            int screenIndex;
            if (!TryParseScreenIndex(tokens[index], out screenIndex))
                continue;

            int s = screenIndex - 1;
            if (s < 0 || s >= provider.SurfaceCount)
                continue;

            // Optional: keep old mapping (but note: key collisions if multiple blocks use Screen=1)
            providerScreens[screenIndex] = provider;

            // CRITICAL: store the provider surface AND the terminal block that owns the CustomData template
            targetSurfaces.Add(new SurfaceTarget
            {
                Surface = provider.GetSurface(s),
                Source  = terminal
            });
        }
    }
}

public void UpdateDisplays()
{
    foreach (var t in targetSurfaces)
    {
        string template = StripIdentifierLines(t.Source.CustomData);
        string output = RenderTemplate(template);
        WriteToSurface(t.Surface, output);
    }
}

public void WriteToSurface(IMyTextSurface surface, string text, bool appendText = false)
{
    surface.ContentType = ContentType.TEXT_AND_IMAGE;

    if (appendText)
        surface.WriteText("\n" + text, true);
    else
        surface.WriteText(text, false);
}

public string RenderTemplate(string template)
{
    if (string.IsNullOrWhiteSpace(template))
        return "";

    string s = template.Replace("\r", "");

    int i = 0;
    while (true)
    {
        int start = s.IndexOf(startArgumentsDelimiter, i);
        if (start < 0) break;

        int end = s.IndexOf(endArgumentsDelimiter, start + 1);
        if (end < 0) break;

        string inner = s.Substring(start + 1, end - (start + 1));

        string replacement = ExpandTokenList(inner);

        s = s.Substring(0, start) + replacement + s.Substring(end + 1);

        i = start + replacement.Length;
    }

    return s;
}

public List<string> GetArgumentTokens(string customData)
{
    var tokens = new List<string>();
    if (string.IsNullOrWhiteSpace(customData))
        return tokens;

    string s = customData.Replace("\r", "");

    int i = 0;
    while (true)
    {
        int lb = s.IndexOf(startArgumentsDelimiter, i);
        if (lb < 0) break;

        int rb = s.IndexOf(endArgumentsDelimiter, lb + 1);
        if (rb < 0) break;

        string inner = s.Substring(lb + 1, rb - (lb + 1));
        string[] parts = inner.Split(newArgumentDelimiter);

        for (int p = 0; p < parts.Length; p++)
        {
            string t = parts[p].Trim();
            if (t.Length == 0) continue;

            t = t.ToUpperInvariant();

            if (!IsKnownToken(t)) continue;

            bool exists = false;
            for (int k = 0; k < tokens.Count; k++)
            {
                if (tokens[k] == t) { exists = true; break; }
            }
            if (!exists)
                tokens.Add(t);
        }

        i = rb + 1;
    }

    return tokens;
}

public bool IsKnownToken(string token)
{
    if (token.StartsWith(surfaceName.ToUpperInvariant()))
        return true;
    switch (token)
    {
        case "TWR":
        case "CANHOVER":
        case "ACCEL":
        case "ROTATING":
        case "STOPDIST":
        case "BRAKEDIST":
        case "ALTDIST":
        case "EARTHLIKE":
        case "PERTRAM":
            return true;
        default:
            return false;
    }
}

public string ExpandTokenList(string inner)
{
    string[] parts = inner.Split(newArgumentDelimiter);

    var sb = new StringBuilder();
    bool first = true;

    foreach (string raw in parts)
    {
        string token = raw.Trim();
        if (token.Length == 0)
            continue;

        string value = ResolveToken(token);

        if (!first)
            sb.Append(" ");
        sb.Append(value);

        first = false;
    }

    return sb.ToString();
}

public string ResolveToken(string token)
{
    
    if (token.StartsWith(surfaceName, StringComparison.OrdinalIgnoreCase))
    return "";

    if (token.Equals("TWR", StringComparison.OrdinalIgnoreCase))
        return DisplayTWR();

    if (token.Equals("CANHOVER", StringComparison.OrdinalIgnoreCase))
        return CanHover();

    if (token.Equals("ACCEL", StringComparison.OrdinalIgnoreCase))
        return DisplayNetAccel().ToString();

    if (token.Equals("STOPDIST", StringComparison.OrdinalIgnoreCase))
        return DisplayStoppingDistance();

    if (token.Equals("BRAKEDIST", StringComparison.OrdinalIgnoreCase))
        return GetDeckBrakeStatusLine();

    if (token.Equals("ALTDIST", StringComparison.OrdinalIgnoreCase))
        return GetDeckAltLine();

    if (token.Equals("EARTHLIKE", StringComparison.OrdinalIgnoreCase))
        return CanHoverOnEarthlike();

    if (token.Equals("PERTRAM", StringComparison.OrdinalIgnoreCase))
        return CanHoverOnPertram();

    return "Unrecognized argument token {" + token + "}";
}

public bool TryParseScreenIndex(string screenArg, out int screenIndex)
{
    int index = surfaceName.Length;

    if (string.IsNullOrEmpty(screenArg))
    {
        Echo($"Please set surfaceName in {Me.DisplayName}");
        screenIndex = -1;
        return false;
    }

    if (index >= screenArg.Length)
    {
        Echo($"\nThe argument {screenArg} does not contain a valid index.");
        screenIndex = -1;
        return false;
    }

    char stringIndex = screenArg[index];
    if(!int.TryParse(stringIndex.ToString(), out screenIndex))
        return false;
    else
        return true;

}

//Handled
// Returns a string indicating whether or not the ship is capable of at least hovering.
public string CanHover()
{
    bool canHover = GetUpAccelNet() >= 0;
    if (canHover)
        return "Yes";
    else
        return "No";
}

// Handled
public string DisplayTWR()
{
    return GetTwr().ToString("0.00");
}

// Handled
public string DisplayNetAccel()
{
    return GetUpAccelNet().ToString("0.00") + "m/s²";
}

// Handled
public string DisplayStoppingDistance()
{
    return GetStoppingDistanceThrustOnly().ToString("0.000") + "m";
}

// Handled
// Display helpers. These are display-only, not physics primitives.
public string GetDeckBrakeStatusLine()
{
    double altNow;
    if (!TryGetAltitudeASL(out altNow))
        return "ALT: N/A";

    double brakeAlt;
    if (!TryGetBrakeAltitudeToOpsDeck(out brakeAlt))
        return "BRAKE ALT: N/A";

    if (double.IsInfinity(brakeAlt))
        return "BRAKE ALT: IMPOSSIBLE";

    double metersToBrake = altNow - brakeAlt;

    if (metersToBrake <= 0)
        return "BRAKE NOW";

    return "Brake in: " + metersToBrake.ToString("0.0") + " m";
}

// Handled
public string GetDeckAltLine()
{
    double deckAlt;
    if (!TryGetOpsDeckAltitude(out deckAlt))
        return "N/A";

    return deckAlt.ToString("0.0") + " m";
}

// Handled
public string CanHoverOnEarthlike()
{
    return CanHoverAtGravity(G_EARTHLIKE)? "Yes" : "No";
}

// Handled
public string CanHoverOnPertram()
{
    return CanHoverAtGravity(G_PERTAM)? "Yes" : "No";
}

//DONT HANDLE
public bool HasToken(List<String> tokens, string token)
{
    return tokens.Contains(token);
}

//DONT HANDLE
public string StripIdentifierLines(string text)
{
    if (string.IsNullOrEmpty(text))
        return "";

    text = text.Replace("\r", "");

    var sb = new StringBuilder();
    string[] lines = text.Split('\n');

    for (int i = 0; i < lines.Length; i++)
    {
        string line = lines[i];

        if (line.IndexOf(controlledBlockIdentifier, StringComparison.OrdinalIgnoreCase) >= 0)
            continue;

        if (sb.Length > 0)
            sb.Append('\n');

        sb.Append(line);
    }

    return sb.ToString();
}

//-------------------------------ALARM AND LIGHTS HANDLING-------------------------------

Color DEFAULTCOLOR = Color.Yellow;
public int internalSoundDelay = SOUND_DELAY;
public const int HIGH_ALERT_DELAY = 15;

double _prevTwr = double.NaN;

public bool BelowTargetTwr()
{
    return GetTwr() < twrLimit;
}

public bool AtOrBelowHardDeck()
{
    return GetTwr() <= HARD_DECK_TWR;
}

public bool ShipDescending()
{
    return GetDownSpeed() > 0;
}

// Shared helper: measured d(TWR)/dt. Negative means TWR is collapsing.
// Returns false until it has a previous sample.
// Resets when not descending (derivative meaningless).
public bool TryGetTwrDot(out double twrNow, out double twrDot)
{
    twrNow = GetTwr();
    twrDot = 0;

    if (!ShipDescending())
    {
        _prevTwr = double.NaN;
        return false;
    }

    double dt = Runtime.TimeSinceLastRun.TotalSeconds;
    if (dt <= 1e-4) dt = 1.0 / 60.0;

    if (double.IsNaN(_prevTwr))
    {
        _prevTwr = twrNow;
        return false;
    }

    twrDot = (twrNow - _prevTwr) / dt;
    _prevTwr = twrNow;
    return true;
}

public bool MustBrakeSoonForOpsTwr()
{
    double vDown = GetDownSpeed();
    if (vDown < 0.1) return false;

    double aNet = GetUpAccelNet();
    if (aNet <= 0.01) return true;

    double twrNow, twrDot;
    if (!TryGetTwrDot(out twrNow, out twrDot))
        return false;

    if (twrDot >= -1e-4) return false;

    double timeToStop = vDown / aNet;
    double timeToLimit = (twrNow - twrLimit) / (-twrDot);

    return timeToStop >= timeToLimit * 0.90;
}

public bool MustBrakeSoonForHardDeck()
{
    double vDown = GetDownSpeed();
    if (vDown < 0.1) return false;

    double aNet = GetUpAccelNet();

    if (aNet <= 0.01) return true;

    double twrNow, twrDot;
    if (!TryGetTwrDot(out twrNow, out twrDot))
        return false;

    if (twrDot >= -1e-4) return false;

    double timeToStop = vDown / aNet;
    double timeToHard = (twrNow - HARD_DECK_TWR) / (-twrDot);

    return timeToStop >= timeToHard * 0.75;
}

public void ProcessLights()
{
    foreach (IMyLightingBlock light in controlledLights)
    {
        var tokens = GetArgumentTokens(light.CustomData);
        bool isRot = HasToken(tokens, "ROTATING");

        if (MustBrakeSoonForOpsTwr() && ShipDescending() && !BelowTargetTwr() && !isRot)
            WarningLightsControl(light, DEFAULTCOLOR);

        else if (BelowTargetTwr() && MustBrakeSoonForHardDeck() && ShipDescending() && !AtOrBelowHardDeck() && !isRot)
            WarningLightsControl(light, Color.Red, blinking: true);

        else if (AtOrBelowHardDeck() && ShipDescending() && !isRot)
            WarningLightsControl(light, Color.Red, blinking: true);

        else if (AtOrBelowHardDeck() && !ShipDescending() && !isRot)
            WarningLightsControl(light, Color.Red);

        else if (BelowTargetTwr() && !AtOrBelowHardDeck() && ShipDescending() && !isRot)
            WarningLightsControl(light, Color.Orange);

        else if (AtOrBelowHardDeck() && isRot)
            WarningLightsControl(light, Color.Red, blinking: false);

        else if (isRot)
            WarningLightsControl(light, Color.Red, false, false);

        else
            WarningLightsControl(light, Color.White);
    }
}

public void ProcessAlarms()
{
    if (playAlertSound)
    {
        foreach (IMySoundBlock alarm in controlledSoundBlocks)
        {
            if ((MustBrakeSoonForHardDeck() && ShipDescending() && !AtOrBelowHardDeck()) ||
                (AtOrBelowHardDeck() && ShipDescending()))
            {
                AlarmSystemControl(alarm, true);
            }
            else if (AtOrBelowHardDeck() && GetDeckBrakeStatusLine() == "BRAKE ALT: IMPOSSIBLE")
            {
                AlarmSystemControl(alarm, true);
            }
        }
    }
}

public void AlarmSystemControl(IMySoundBlock alarm, bool highAlert = false)
{
    if (--internalSoundDelay <= 0)
    {
        if (!alarm.Enabled)
            alarm.Enabled = true;

        SetAlarmSound(alarm);
        alarm.Play();

        internalSoundDelay = highAlert ? HIGH_ALERT_DELAY : SOUND_DELAY;
    }
}

public void SetAlarmSound(IMySoundBlock alarm)
{
    if (alarm.SelectedSound != alertSound || !alarm.IsSoundSelected)
        alarm.SelectedSound = alertSound;
}

public void WarningLightsControl(IMyLightingBlock light, Color color, bool enabled = true, bool blinking = false)
{
    if (light.BlinkIntervalSeconds != (float)warningLightBlink && blinking)
    {
        light.BlinkIntervalSeconds = (float)warningLightBlink;
    }
    else if (!blinking)
    {
        light.BlinkIntervalSeconds = 0.0f;
    }

    light.Color = color;
    light.BlinkLength = 0.2f;

    IMyFunctionalBlock lightTerminal = light;
    lightTerminal.Enabled = enabled;
}

public int tickTimer = 0;
public string TickTimer()
{
    tickTimer++;
    if (tickTimer >= 720) tickTimer = 0;
    if (tickTimer < 60) return "Running.   ---";
    if (tickTimer < 120) return "Running..   \\";
    if (tickTimer < 180) return "Running...  |";
    if (tickTimer < 240) return "Running.    /";
    if (tickTimer < 300) return "Running..  ---";
    if (tickTimer < 360) return "Running...  \\";
    if (tickTimer < 420) return "Running.    |";
    if (tickTimer < 480) return "Running..   /";
    if (tickTimer < 540) return "Running... ---";
    if (tickTimer < 600) return "Running.    \\";
    if (tickTimer < 660) return "Running..   |";
    else return "Running...  /";
}

//-------------------------------------PHYSICS MATH--------------------------------------

// Planet gravity approximation constants (vanilla-ish defaults)
const double DEFAULT_HILL_FRACTION = 0.12;
const double DEFAULT_FALLOFF = 7.0;
const double G_EPS = 1e-6;
const double G_EARTHLIKE = 9.8;
const double G_PERTAM    = 11.77;


// Absolute hard deck TWR: below this, you have zero upward net authority.
//
const double HARD_DECK_TWR = 1.1;

// Either returns the main cockpit, or any ship controller.
// It doesn't matter for these calculations, but main cockpit is more semantically correct.
//
// Returns: the main cockpit if one exists, otherwise the first ship controller found.
public IMyShipController GetShipController()
{
    List<IMyShipController> shipControllers = new List<IMyShipController>();
    GridTerminalSystem.GetBlocksOfType(shipControllers);

    foreach (IMyShipController controller in shipControllers)
    {
        if (controller.IsMainCockpit)
            return controller;
    }

    return shipControllers[0];
}

// Returns the ship's Thrust-to-Weight Ratio (TWR) against current natural gravity.
//
// Meaning:
//   > 1.0 : can accelerate upward / climb
//   = 1.0 : neutral (no upward authority margin)
//   < 1.0 : cannot arrest descent / cannot climb
//
// Returns: PositiveInfinity when gravity is effectively zero.
public double GetTwr()
{
    double g = GetGravityVector().Length();
    if (g < 1e-6) return double.PositiveInfinity;

    double m = GetPhysicalMassKg();
    double fUp = GetAntiGravityThrustNewtons();
    return fUp / (m * g);
}

// Computes total effective upward thrust (Newtons) that opposes gravity *right now*.
//
// Only counts thrusters that are functional + enabled + working,
// and only counts the thrust component aligned opposite gravity.
//
// Returns: 0 if no gravity or no usable upward thrust.
public double GetAntiGravityThrustNewtons()
{
    IMyShipController controller = GetShipController();
    if (controller == null) return 0;

    Vector3D gVec = controller.GetNaturalGravity();
    if (gVec.LengthSquared() < 1e-6) return 0;

    Vector3D upDir = -Vector3D.Normalize(gVec);

    List<IMyThrust> thrusters = new List<IMyThrust>();
    GridTerminalSystem.GetBlocksOfType(thrusters, t => t.CubeGrid == Me.CubeGrid);

    double antiG = 0;

    for (int i = 0; i < thrusters.Count; i++)
    {
        IMyThrust t = thrusters[i];

        if (!t.IsFunctional || !t.Enabled || !t.IsWorking)
            continue;

        Vector3D pushDir = t.WorldMatrix.Backward;
        double align = pushDir.Dot(upDir);

        if (align > 0)
            antiG += t.MaxEffectiveThrust * align;
    }

    return antiG;
}

// Returns the ship's physical mass in kilograms.
//
// Returns: PhysicalMass (kg) as used by the physics engine.
public float GetPhysicalMassKg()
{
    return GetShipController().CalculateShipMass().PhysicalMass;
}

// Returns the stopping distance (meters) required to null the ship's current velocity vector,
// assuming thrust-only braking (gravity ignored).
//
// Uses: d = v^2 / (2a)
//
// Returns:
//   0                if speed is negligible
//   PositiveInfinity if no braking thrust is available
public double GetStoppingDistanceThrustOnly()
{
    IMyShipController c = GetShipController();
    if (c == null) return 0;

    double v = c.GetShipVelocities().LinearVelocity.Length();
    double a = GetBrakingDecelThrustOnly();

    if (v < 1e-3) return 0;
    if (a < 1e-6) return double.PositiveInfinity;

    return (v * v) / (2.0 * a);
}

// Returns the maximum deceleration (m/s^2) achievable by thrust alone,
// braking directly opposite the current velocity vector (gravity ignored).
//
// Returns: 0 if stationary or no usable braking thrust.
public double GetBrakingDecelThrustOnly()
{
    IMyShipController c = GetShipController();
    if (c == null) return 0;

    Vector3D v = c.GetShipVelocities().LinearVelocity;
    double speed = v.Length();
    if (speed < 1e-3) return 0;

    Vector3D brakeDir = -Vector3D.Normalize(v);

    List<IMyThrust> thrusters = new List<IMyThrust>();
    GridTerminalSystem.GetBlocksOfType(thrusters, t => t.CubeGrid == Me.CubeGrid);

    double brakeForce = 0;

    for (int i = 0; i < thrusters.Count; i++)
    {
        IMyThrust t = thrusters[i];

        if (!t.IsFunctional || !t.Enabled || !t.IsWorking)
            continue;

        Vector3D pushDir = t.WorldMatrix.Backward;
        double align = pushDir.Dot(brakeDir);

        if (align > 0)
            brakeForce += t.MaxEffectiveThrust * align;
    }

    double m = GetPhysicalMassKg();
    if (m <= 1e-3) return 0;

    return brakeForce / m;
}

// Returns the current natural gravity vector at the ship's position.
// Direction points "down" (toward gravity source); magnitude is m/s^2.
public Vector3D GetGravityVector()
{
    return GetShipController().GetNaturalGravity();
}

// Returns net upward acceleration (m/s^2) if you full-burn upward against gravity.
//
// Meaning:
//   > 0 : climbing capability exists
//   = 0 : neutral (no vertical authority margin)
//   < 0 : cannot climb / will descend if moving down
//
// Returns: (F_up / m) - g
public double GetUpAccelNet()
{
    double g = GetGravityVector().Length();
    double m = GetPhysicalMassKg();
    double fUp = GetAntiGravityThrustNewtons();
    return (fUp / m) - g;
}

// Returns the ship's current available upward acceleration ignoring gravity (F_up / m) in m/s^2.
// This is the same quantity you'd compare against gravity to decide a TWR boundary.
//
// Returns: F_up / m (m/s^2)
public double GetUpAccelThrustOnly()
{
    double m = GetPhysicalMassKg();
    if (m <= 1e-6) return 0;
    return GetAntiGravityThrustNewtons() / m;
}

// Solves the gravity falloff model to find the altitude (ASL) where natural gravity equals gTarget.
//
// Uses a vanilla-style hill + power-law falloff approximation.
// Returns false if not in a gravity well or gravity too small to model reliably.
//
// Returns:
//   true  + altTarget (meters ASL) on success
//   false if altitude cannot be solved
public bool TryGetAltitudeToGravity(double gTarget, out double altTarget)
{
    altTarget = 0;

    IMyShipController c = GetShipController();
    if (c == null) return false;

    Vector3D planetCenter;
    double altNow;
    if (!c.TryGetPlanetPosition(out planetCenter)) return false;
    if (!c.TryGetPlanetElevation(MyPlanetElevation.Sealevel, out altNow)) return false;

    Vector3D pos = c.GetPosition();
    double rNow = Vector3D.Distance(pos, planetCenter);

    double R = rNow - altNow;
    if (R < 1) return false;

    double gNow = c.GetNaturalGravity().Length();

    double b = gNow;
    if (b < 0.1) return false;

    if (gTarget < G_EPS) return false;

    double MaxR = R * (1.0 + DEFAULT_HILL_FRACTION);

    if (gTarget >= b)
    {
        altTarget = MaxR - R;
        return true;
    }

    double ratio = b / gTarget;
    double rTarget = MaxR * Math.Pow(ratio, 1.0 / DEFAULT_FALLOFF);

    altTarget = rTarget - R;
    return true;
}

// Returns the ship's current altitude above sea level (meters).
//
// Returns:
//   true  + altNow (meters ASL) on success
//   false if no controller / not in gravity well context
public bool TryGetAltitudeASL(out double altNow)
{
    altNow = 0;

    IMyShipController c = GetShipController();
    if (c == null) return false;

    return c.TryGetPlanetElevation(MyPlanetElevation.Sealevel, out altNow);
}

public double GetMaxUpAccelThrustOnly()
{
    double m = GetPhysicalMassKg();
    if (m <= 1e-6) return 0;

    double fMax = GetMaxAxisThrustNewtons();
    return fMax / m; // m/s^2
}


public double GetMaxAxisThrustNewtons()
{
    IMyShipController c = GetShipController();
    if (c == null) return 0;

    // Candidate "up" directions we might point against gravity.
    // We assume the ship can reorient so its strongest axis becomes "up".
    Vector3D[] axes =
    {
        c.WorldMatrix.Up,
        c.WorldMatrix.Down,
        c.WorldMatrix.Left,
        c.WorldMatrix.Right,
        c.WorldMatrix.Forward,
        c.WorldMatrix.Backward
    };

    double max = 0;
    for (int i = 0; i < axes.Length; i++)
    {
        double f = GetThrustAlongDirNewtons(Vector3D.Normalize(axes[i]));
        if (f > max) max = f;
    }

    return max;
}

public double GetThrustAlongDirNewtons(Vector3D desiredPushDir)
{
    // desiredPushDir: direction we want the ship to accelerate (i.e., "up")
    // Thrusters push along t.WorldMatrix.Backward.
    if (desiredPushDir.LengthSquared() < 1e-6) return 0;

    List<IMyThrust> thrusters = new List<IMyThrust>();
    GridTerminalSystem.GetBlocksOfType(thrusters, t => t.CubeGrid == Me.CubeGrid);

    double total = 0;

    for (int i = 0; i < thrusters.Count; i++)
    {
        IMyThrust t = thrusters[i];
        if (!t.IsFunctional || !t.Enabled || !t.IsWorking)
            continue;

        Vector3D pushDir = t.WorldMatrix.Backward;
        double align = pushDir.Dot(desiredPushDir);

        if (align > 0)
            total += t.MaxEffectiveThrust * align;
    }

    return total;
}


// Returns the current downward speed in m/s along the gravity vector.
// Positive = descending; 0 = climbing/level/no gravity.
public double GetDownSpeed()
{
    IMyShipController c = GetShipController();
    if (c == null) return 0;

    Vector3D gVec = c.GetNaturalGravity();
    if (gVec.LengthSquared() < 1e-6) return 0;

    Vector3D downDir = Vector3D.Normalize(gVec);
    Vector3D vel = c.GetShipVelocities().LinearVelocity;

    double vDown = vel.Dot(downDir);
    if (vDown < 0) vDown = 0;

    return vDown;
}

// Returns the vertical stopping distance (meters of altitude) required to null current downward speed,
// assuming you immediately apply maximum upward thrust.
//
// Returns:
//   0                if not descending
//   PositiveInfinity if descent cannot be arrested (no positive net upward accel)
public double GetVerticalStopDistance()
{
    double vDown = GetDownSpeed();
    double aNet = GetUpAccelNet();

    if (vDown < 1e-3) return 0;
    if (aNet <= 0) return double.PositiveInfinity;

    return (vDown * vDown) / (2.0 * aNet);
}


// Converts a TWR threshold into the gravity magnitude (m/s^2)
// that corresponds to that TWR for the CURRENT ship.
//
// TWR = (F/m) / g  =>  g_at_twr = (F/m) / twrThreshold
//
// Returns:
//   PositiveInfinity if twrThreshold is invalid or F/m is not meaningful
public double GetGravityAtTwr(double twrThreshold)
{
    if (twrThreshold <= 1e-6) return double.PositiveInfinity;

    double fOverM = GetUpAccelThrustOnly();
    if (fOverM <= 1e-6) return double.PositiveInfinity;

    return fOverM / twrThreshold;
}

// Generic deck altitude solver for a provided TWR threshold.
// (Internal utility; keep it parameterized to avoid duplicating math.)
public bool TryGetDeckAltitudeFromTwr(double twrThreshold, out double deckAltASL)
{
    deckAltASL = 0;

    double gDeck = GetGravityAtTwr(twrThreshold);
    if (double.IsInfinity(gDeck)) return false;

    return TryGetAltitudeToGravity(gDeck, out deckAltASL);
}

// --------- OPERATIONAL (USER) DECK: uses twrLimit implicitly ---------

public bool TryGetOpsDeckAltitude(out double opsDeckAltASL)
{
    return TryGetDeckAltitudeFromTwr(twrLimit, out opsDeckAltASL);
}

public bool TryGetBrakeAltitudeToOpsDeck(out double brakeAltASL)
{
    brakeAltASL = 0;

    double deckAlt;
    if (!TryGetOpsDeckAltitude(out deckAlt))
        return false;

    double stopDist = GetVerticalStopDistance();
    if (double.IsInfinity(stopDist))
    {
        brakeAltASL = double.PositiveInfinity;
        return true;
    }

    brakeAltASL = deckAlt + stopDist;
    return true;
}


// --------- HARD DECK: absolute recoverability limit (HARD_DECK_TWR) ---------

public bool TryGetHardDeckAltitude(out double hardDeckAltASL)
{
    return TryGetDeckAltitudeFromTwr(HARD_DECK_TWR, out hardDeckAltASL);
}

public bool TryGetBrakeAltitudeToHardDeck(out double brakeAltASL)
{
    brakeAltASL = 0;

    double deckAlt;
    if (!TryGetHardDeckAltitude(out deckAlt))
        return false;

    double stopDist = GetVerticalStopDistance();
    if (double.IsInfinity(stopDist))
    {
        brakeAltASL = double.PositiveInfinity;
        return true;
    }

    brakeAltASL = deckAlt + stopDist;
    return true;
}

private bool CanHoverAtGravity(double gravity)
{
    double fOverM = GetMaxUpAccelThrustOnly();
    return fOverM >= gravity;
}

}
}
