using CommunityToolkit.Mvvm.ComponentModel;
using MathNet.Filtering.FIR;

namespace SpaceCraneControl
{
    public partial class FilteredAngle : ObservableObject
    {
        public FilteredAngle()
        {
            Filter = new OnlineFirFilter(Enumerable.Repeat(0.1, 10).ToArray());
        }

        [ObservableProperty]
        double angle = 0;
        [ObservableProperty]
        double filtered = 0;
        public OnlineFirFilter Filter { get; set; }

        public double Process(double val)
        {
            Angle = val;
            Filtered = Filter.ProcessSample(val);
            return Filtered;
        }
    }
}