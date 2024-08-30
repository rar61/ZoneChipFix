using Torch;
using Torch.Views;

namespace ZoneChipFix
{
    public class Config : ViewModel
    {
        float chipConsumptionMultiplier = 1;
        [Display(Name = "Chip Consumption Multiplier", Description = "Scales the amount of chips consumed.")]
        public float ChipConsumptionMultiplier {
            get => chipConsumptionMultiplier;
            set
            {
                SetValue(ref chipConsumptionMultiplier, value);
                ZoneChipFixPatch.ChipConsumptionMultiplier = value;
            }
        }
    }
}
