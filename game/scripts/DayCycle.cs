using System;
using Godot;
using Rts.Sim;

namespace Rts.Game;

/// <summary>
/// The time of day and the weather as they look. The clock and the weather are game rules (see
/// <see cref="Conditions"/>): they follow the simulation tick, so a replay or a paused game shows the same
/// time and weather as the match it came from, and the speed buttons speed them up. This turns them into
/// hours and a smooth change of sky for the look of the world (see <see cref="WorldLighting"/> and
/// <see cref="Ambience"/>). With day and night off for the match, it is always late morning.
///
/// The sun is up from <see cref="Sunrise"/> to <see cref="Sunset"/>, so night is the shorter part of the
/// day; at night the moon lights the world in blue and lanterns glow at the doors.
/// </summary>
public sealed class DayCycle
{
    public const float Sunrise = 5.5f, Sunset = 20.5f;

    private readonly Simulation _sim;

    public DayCycle(Simulation sim) => _sim = sim;

    /// <summary>Always day: the player chose to keep the world lit as at <see cref="DayHour"/> (the look only).</summary>
    public bool AlwaysDay { get; set; }
    /// <summary>The hour shown while <see cref="AlwaysDay"/>, or with day and night off: late morning.</summary>
    public const float DayHour = 11f;
    /// <summary>The weather shown whatever the match's weather (for screenshots), or null for the match's.</summary>
    public Weather? WeatherOverride { get; set; }

    /// <summary>True if the match has day, night and weather.</summary>
    public bool On => _sim.DayNight;

    /// <summary>Hour of the day (0 to 24) the world looks lit for at <paramref name="tick"/>, with <paramref name="alpha"/> of the next.</summary>
    public float Hour(long tick, float alpha = 0)
    {
        if (AlwaysDay || !On) return DayHour;
        return TrueHour(tick, alpha);
    }

    /// <summary>The match's own hour of the day at <paramref name="tick"/> (the rules', whatever <see cref="AlwaysDay"/> says).</summary>
    public float TrueHour(long tick, float alpha = 0)
    {
        if (!On) return Conditions.StartMinute / 60f;
        double minutes = _sim.DayStart + (tick + alpha) * 1440.0 / Conditions.DayTicks;
        return (float)(minutes / 60.0 % 24.0);
    }

    /// <summary>Which day of the match it is at <paramref name="tick"/>, counting from 1.</summary>
    public int Day(long tick) => 1 + (int)((_sim.DayStart * (long)Conditions.DayTicks / 1440 + tick) / Conditions.DayTicks);

    /// <summary>The weather now, as shown.</summary>
    public Weather Weather => WeatherOverride ?? _sim.Conditions.Weather;

    /// <summary>Ticks over which the sky turns from one weather to the next, ahead of the change in the game.</summary>
    public const int FadeTicks = Conditions.HourTicks / 2;

    /// <summary>
    /// How the sky looks now: in the last <see cref="FadeTicks"/> of a spell it turns toward the next
    /// weather (see <see cref="Conditions.Next"/>), so rain can be seen coming before it arrives.
    /// </summary>
    public WeatherLook SkyAt(long tick, float alpha = 0)
    {
        var now = Of(Weather);
        if (WeatherOverride != null || !On) return now;
        var c = _sim.Conditions;
        float left = c.ChangesAt - tick - alpha;
        if (left >= FadeTicks || c.Next == c.Weather) return now;
        float t = Mathf.SmoothStep(0f, 1f, 1f - Mathf.Max(0f, left) / FadeTicks);
        return now.Lerp(Of(c.Next), t);
    }

    /// <summary>
    /// The look of a weather: Cloud (0 to 1) greys the sky and dims the sun, Rain (0 to 1) is how hard it
    /// rains, Mist (0 to 1) thickens the haze, and Wind (0 to 1) sways the trees and blows smoke and rain.
    /// </summary>
    public readonly record struct WeatherLook(float Cloud, float Rain, float Mist, float Wind)
    {
        public WeatherLook Lerp(WeatherLook b, float t) => new(Mathf.Lerp(Cloud, b.Cloud, t), Mathf.Lerp(Rain, b.Rain, t),
            Mathf.Lerp(Mist, b.Mist, t), Mathf.Lerp(Wind, b.Wind, t));
    }

    public static WeatherLook Of(Weather w) => w switch
    {
        Weather.Cloudy => new WeatherLook(0.55f, 0f, 0.1f, 0.35f),
        Weather.Rain => new WeatherLook(0.8f, 0.6f, 0.25f, 0.45f),
        Weather.Storm => new WeatherLook(1f, 1f, 0.35f, 1f),
        Weather.Fog => new WeatherLook(0.45f, 0f, 1f, 0.05f),
        _ => new WeatherLook(0f, 0f, 0f, 0.2f),
    };

    /// <summary>A word for a weather, for the HUD.</summary>
    public static string Name(Weather w) => w switch
    {
        Weather.Cloudy => "Cloudy",
        Weather.Rain => "Rain",
        Weather.Storm => "Storm",
        Weather.Fog => "Fog",
        _ => "Clear",
    };

    public static bool IsNight(float hour) => hour < Sunrise || hour >= Sunset;

    /// <summary>What part of the day an hour is, for the HUD.</summary>
    public static string PartOf(float hour) => hour switch
    {
        < 5f => "Night",
        < 7.5f => "Dawn",
        < 11f => "Morning",
        < 14f => "Noon",
        < 18f => "Afternoon",
        < 21f => "Dusk",
        _ => "Night",
    };

    /// <summary>The light at one hour of the day.</summary>
    public readonly record struct Light(
        Color Sun, float SunEnergy, Color Ambient, float AmbientEnergy,
        Color SkyTop, Color SkyHorizon, Color Haze, float Lanterns);

    // Keyframes through the day; the light in between is blended. Colours are picked against the mood board:
    // a warm golden morning and evening, a clear bright noon, an orange dusk and a cool blue moonlit night
    // that is still light enough to play in.
    private static readonly (float Hour, Light Light)[] Keys =
    {
        (0f, new(C(0.62f, 0.70f, 1f), 0.45f, C(0.30f, 0.40f, 0.85f), 0.70f, C(0.03f, 0.05f, 0.12f), C(0.10f, 0.14f, 0.26f), C(0.10f, 0.13f, 0.22f), 1f)),
        (4.5f, new(C(0.62f, 0.70f, 1f), 0.45f, C(0.30f, 0.40f, 0.85f), 0.70f, C(0.03f, 0.05f, 0.12f), C(0.10f, 0.14f, 0.26f), C(0.10f, 0.13f, 0.22f), 1f)),
        (5.5f, new(C(1f, 0.62f, 0.48f), 0.08f, C(0.56f, 0.55f, 0.72f), 0.72f, C(0.18f, 0.24f, 0.42f), C(0.85f, 0.58f, 0.50f), C(0.52f, 0.46f, 0.52f), 0.8f)),
        (6.5f, new(C(1f, 0.66f, 0.42f), 0.75f, C(0.62f, 0.58f, 0.66f), 0.65f, C(0.30f, 0.45f, 0.72f), C(0.98f, 0.72f, 0.52f), C(0.82f, 0.68f, 0.60f), 0.1f)),
        (8.5f, new(C(1f, 0.88f, 0.70f), 1.0f, C(0.66f, 0.72f, 0.82f), 0.55f, C(0.32f, 0.52f, 0.84f), C(0.78f, 0.84f, 0.90f), C(0.72f, 0.78f, 0.86f), 0f)),
        (13f, new(C(1f, 0.97f, 0.90f), 1.15f, C(0.68f, 0.76f, 0.88f), 0.55f, C(0.28f, 0.50f, 0.86f), C(0.74f, 0.84f, 0.94f), C(0.72f, 0.80f, 0.90f), 0f)),
        (17.5f, new(C(1f, 0.82f, 0.58f), 1.05f, C(0.68f, 0.66f, 0.72f), 0.55f, C(0.30f, 0.46f, 0.78f), C(0.96f, 0.78f, 0.56f), C(0.84f, 0.74f, 0.62f), 0f)),
        (19.3f, new(C(1f, 0.66f, 0.42f), 0.80f, C(0.62f, 0.62f, 0.70f), 0.6f, C(0.26f, 0.32f, 0.56f), C(1f, 0.62f, 0.42f), C(0.70f, 0.58f, 0.54f), 0.5f)),
        (20.5f, new(C(0.95f, 0.50f, 0.40f), 0.08f, C(0.52f, 0.52f, 0.74f), 0.70f, C(0.08f, 0.10f, 0.24f), C(0.42f, 0.30f, 0.38f), C(0.26f, 0.24f, 0.34f), 1f)),
        (21.5f, new(C(0.62f, 0.70f, 1f), 0.45f, C(0.30f, 0.40f, 0.85f), 0.70f, C(0.03f, 0.05f, 0.12f), C(0.10f, 0.14f, 0.26f), C(0.10f, 0.13f, 0.22f), 1f)),
        (24f, new(C(0.62f, 0.70f, 1f), 0.45f, C(0.30f, 0.40f, 0.85f), 0.70f, C(0.03f, 0.05f, 0.12f), C(0.10f, 0.14f, 0.26f), C(0.10f, 0.13f, 0.22f), 1f)),
    };

    private static Color C(float r, float g, float b) => new(r, g, b);

    /// <summary>The light at <paramref name="hour"/>, blended between the keyframes either side.</summary>
    public static Light LightAt(float hour)
    {
        hour = ((hour % 24f) + 24f) % 24f;
        int i = 0;
        while (i < Keys.Length - 2 && Keys[i + 1].Hour <= hour) i++;
        var (h0, a) = Keys[i];
        var (h1, b) = Keys[i + 1];
        float t = Mathf.SmoothStep(0f, 1f, (hour - h0) / Math.Max(0.001f, h1 - h0));
        return new Light(
            a.Sun.Lerp(b.Sun, t), Mathf.Lerp(a.SunEnergy, b.SunEnergy, t),
            a.Ambient.Lerp(b.Ambient, t), Mathf.Lerp(a.AmbientEnergy, b.AmbientEnergy, t),
            a.SkyTop.Lerp(b.SkyTop, t), a.SkyHorizon.Lerp(b.SkyHorizon, t), a.Haze.Lerp(b.Haze, t),
            Mathf.Lerp(a.Lanterns, b.Lanterns, t));
    }

    /// <summary>
    /// Where the main light shines from at <paramref name="hour"/>, as (pitch, yaw) in degrees for a
    /// directional light: by day the sun, rising in the east and setting in the west, never so low that
    /// shadows streak across the whole map; by night the moon, low in the south-west, where it can be seen
    /// with the camera tilted down to the horizon. The pixel sun and moon in the sky sit along it.
    /// </summary>
    public static (float Pitch, float Yaw) Direction(float hour)
    {
        if (IsNight(hour)) return (-14f, 200f);
        float t = (hour - Sunrise) / (Sunset - Sunrise);
        float elevation = Mathf.Max(12f, Mathf.Sin(t * Mathf.Pi) * 68f);
        float yaw = Mathf.Lerp(105f, -45f, t);
        return (-elevation, yaw);
    }
}
