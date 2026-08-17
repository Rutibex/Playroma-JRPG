using PlayromaMusic.Core.AdvancedComposition;
using PlayromaMusic.Core.StructuredComposition;

namespace PlayromaMusic.Generators.Advanced;

public sealed class AdaptiveTensionPlanner
{
    public IReadOnlyList<TensionPoint> Apply(
        IReadOnlyList<FormSectionPlan> sections,
        IList<HarmonicEventPlan> harmony,
        AdaptiveTensionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(harmony);
        ArgumentNullException.ThrowIfNull(settings);
        if (sections.Count == 0) return Array.Empty<TensionPoint>();

        int totalBars = sections.Max(section => section.StartBar + section.LengthBars);
        int samplesPerBar = Math.Clamp(settings.SamplesPerBar, 1, 16);
        int totalSamples = Math.Max(1, totalBars * samplesPerBar);
        int climaxSample = Math.Clamp((int)Math.Round((totalSamples - 1) * settings.ClimaxPosition), 0, totalSamples - 1);
        int climaxBar = climaxSample / samplesPerBar;
        List<TensionPoint> curve = new(totalBars);
        double previous = sections[0].Energy;
        for (int bar = 0; bar < totalBars; bar++)
        {
            FormSectionPlan section = sections.Last(item => item.StartBar <= bar);
            double accumulated = 0;
            for (int sample = 0; sample < samplesPerBar; sample++)
            {
                int absoluteSample = bar * samplesPerBar + sample;
                double position = totalSamples <= 1 ? 0 : absoluteSample / (double)(totalSamples - 1);
                double climaxDistance = Math.Abs(absoluteSample - climaxSample) / (double)Math.Max(1, totalSamples);
                double climax = settings.ClimaxStrength * Math.Exp(-climaxDistance * 9.0);
                bool cadenceSample = sample == samplesPerBar - 1 && harmony.Any(item => item.StartBar == bar && item.Cadence is CadenceKind.Authentic or CadenceKind.Half);
                double cadenceLift = cadenceSample ? 0.13 : 0;
                double resolution = position > 0.90 ? settings.ResolutionStrength * ((position - 0.90) / 0.10) : 0;
                double raw = Math.Clamp(section.Energy * 0.57 + climax * 0.43 + cadenceLift - resolution, 0.02, 1.0);
                previous = Math.Clamp(previous * settings.Smoothing + raw * (1.0 - settings.Smoothing), 0.0, 1.0);
                accumulated += previous;
            }
            curve.Add(new TensionPoint
            {
                Bar = bar,
                Value = accumulated / samplesPerBar,
                Source = bar == climaxBar ? "climax" : section.Kind.ToString()
            });
        }

        foreach (HarmonicEventPlan item in harmony)
        {
            double adaptive = curve[Math.Clamp(item.StartBar, 0, curve.Count - 1)].Value;
            item.Tension = Math.Clamp(item.Tension * 0.45 + adaptive * 0.55, 0.0, 1.0);
            if (adaptive > 0.82 && item.ChordToneCount < 4) item.ChordToneCount = 4;
        }

        foreach (FormSectionPlan section in sections)
        {
            double average = curve.Skip(section.StartBar).Take(section.LengthBars).Select(point => point.Value).DefaultIfEmpty(section.Energy).Average();
            section.Energy = Math.Clamp(section.Energy * 0.55 + average * 0.45, 0.0, 1.0);
        }
        return curve;
    }
}
