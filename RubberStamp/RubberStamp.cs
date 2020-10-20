using System;
using System.Drawing;
using System.Reflection;
using System.Collections.Generic;
using PaintDotNet;
using PaintDotNet.Effects;
using PaintDotNet.IndirectUI;
using PaintDotNet.PropertySystem;

namespace RubberStampEffect
{
    public class PluginSupportInfo : IPluginSupportInfo
    {
        public string Author => base.GetType().Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>().Copyright;
        public string Copyright => base.GetType().Assembly.GetCustomAttribute<AssemblyDescriptionAttribute>().Description;
        public string DisplayName => base.GetType().Assembly.GetCustomAttribute<AssemblyProductAttribute>().Product;
        public Version Version => base.GetType().Assembly.GetName().Version;
        public Uri WebsiteUri => new Uri("https://forums.getpaint.net/topic/111225-rubber-stamp/");
    }

    [PluginSupportInfo(typeof(PluginSupportInfo), DisplayName = "Rubber Stamp")]
    public class RubberStampEffectPlugin : PropertyBasedEffect
    {
        private int scale = 50;
        private double roughness = 1.0;
        private int minOpacity = 85;
        private int reseed = 0;
        private bool customColor = false;
        private ColorBgra color = ColorBgra.Black;

        private CloudsEffect cloudsEffect = new CloudsEffect();
        private PropertyCollection cloudsProps;
        private Surface emptySurface;
        private Surface cloudSurface;

        private static readonly Image StaticIcon = new Bitmap(typeof(RubberStampEffectPlugin), "RubberStamp.png");

        public RubberStampEffectPlugin()
            : base("Rubber Stamp", StaticIcon, "Object", new EffectOptions { Flags = EffectFlags.Configurable })
        {
        }

        private enum PropertyNames
        {
            Scale,
            Roughness,
            MinimumOpacity,
            Reseed,
            UseCustomColor,
            Color
        }

        protected override PropertyCollection OnCreatePropertyCollection()
        {
            ColorBgra PrimaryColor = EnvironmentParameters.PrimaryColor.NewAlpha(255);

            List<Property> props = new List<Property>
            {
                new Int32Property(PropertyNames.Scale, 50, 3, 100),
                new DoubleProperty(PropertyNames.Roughness, 1.0, 0, 1.0),
                new Int32Property(PropertyNames.MinimumOpacity, 85, byte.MinValue, byte.MaxValue),
                new Int32Property(PropertyNames.Reseed, 0, 0, 255),
                new BooleanProperty(PropertyNames.UseCustomColor, false),
                new Int32Property(PropertyNames.Color, ColorBgra.ToOpaqueInt32(PrimaryColor), 0, 0xffffff)
            };

            List<PropertyCollectionRule> propRules = new List<PropertyCollectionRule>
            {
                new ReadOnlyBoundToBooleanRule(PropertyNames.Color, PropertyNames.UseCustomColor, true)
            };

            return new PropertyCollection(props, propRules);
        }

        protected override ControlInfo OnCreateConfigUI(PropertyCollection props)
        {
            ControlInfo configUI = CreateDefaultConfigUI(props);

            configUI.SetPropertyControlValue(PropertyNames.Scale, ControlInfoPropertyNames.DisplayName, "Scale");
            configUI.SetPropertyControlValue(PropertyNames.Roughness, ControlInfoPropertyNames.DisplayName, "Roughness");
            configUI.SetPropertyControlValue(PropertyNames.Roughness, ControlInfoPropertyNames.SliderLargeChange, 0.25);
            configUI.SetPropertyControlValue(PropertyNames.Roughness, ControlInfoPropertyNames.SliderSmallChange, 0.05);
            configUI.SetPropertyControlValue(PropertyNames.Roughness, ControlInfoPropertyNames.UpDownIncrement, 0.01);
            configUI.SetPropertyControlValue(PropertyNames.Roughness, ControlInfoPropertyNames.DecimalPlaces, 3);
            configUI.SetPropertyControlValue(PropertyNames.MinimumOpacity, ControlInfoPropertyNames.DisplayName, "Minimum Opacity");
            configUI.SetPropertyControlValue(PropertyNames.MinimumOpacity, ControlInfoPropertyNames.ControlColors, new ColorBgra[] { ColorBgra.White, ColorBgra.Black });
            configUI.SetPropertyControlValue(PropertyNames.Reseed, ControlInfoPropertyNames.DisplayName, string.Empty);
            configUI.SetPropertyControlType(PropertyNames.Reseed, PropertyControlType.IncrementButton);
            configUI.SetPropertyControlValue(PropertyNames.Reseed, ControlInfoPropertyNames.ButtonText, "Reseed");
            configUI.SetPropertyControlValue(PropertyNames.UseCustomColor, ControlInfoPropertyNames.DisplayName, "Color");
            configUI.SetPropertyControlValue(PropertyNames.UseCustomColor, ControlInfoPropertyNames.Description, "Use Custom Color");
            configUI.SetPropertyControlValue(PropertyNames.Color, ControlInfoPropertyNames.DisplayName, string.Empty);
            configUI.SetPropertyControlType(PropertyNames.Color, PropertyControlType.ColorWheel);

            return configUI;
        }

        protected override void OnSetRenderInfo(PropertyBasedEffectConfigToken newToken, RenderArgs dstArgs, RenderArgs srcArgs)
        {
            scale = newToken.GetProperty<Int32Property>(PropertyNames.Scale).Value;
            roughness = newToken.GetProperty<DoubleProperty>(PropertyNames.Roughness).Value;
            minOpacity = newToken.GetProperty<Int32Property>(PropertyNames.MinimumOpacity).Value;
            reseed = (byte)newToken.GetProperty<Int32Property>(PropertyNames.Reseed).Value;
            customColor = newToken.GetProperty<BooleanProperty>(PropertyNames.UseCustomColor).Value;
            color = ColorBgra.FromOpaqueInt32(newToken.GetProperty<Int32Property>(PropertyNames.Color).Value);

            if (emptySurface == null)
                emptySurface = new Surface(srcArgs.Size);
            if (cloudSurface == null)
                cloudSurface = new Surface(srcArgs.Size);

            // Call the Render Clouds function
            cloudsProps = cloudsEffect.CreatePropertyCollection();
            PropertyBasedEffectConfigToken CloudsParameters = new PropertyBasedEffectConfigToken(cloudsProps);
            CloudsParameters.SetPropertyValue(CloudsEffect.PropertyNames.Scale, scale);
            CloudsParameters.SetPropertyValue(CloudsEffect.PropertyNames.Power, roughness);
            CloudsParameters.SetPropertyValue(CloudsEffect.PropertyNames.Seed, reseed);
            using (EffectEnvironmentParameters environParameters = new EffectEnvironmentParameters(ColorBgra.Black, Color.FromArgb(minOpacity, Color.Black), 0, EnvironmentParameters.GetSelectionAsPdnRegion(), emptySurface))
                cloudsEffect.EnvironmentParameters = environParameters;
            cloudsEffect.SetRenderInfo(CloudsParameters, new RenderArgs(cloudSurface), new RenderArgs(emptySurface));

            base.OnSetRenderInfo(newToken, dstArgs, srcArgs);
        }

        protected override void OnRender(Rectangle[] renderRects, int startIndex, int length)
        {
            if (length == 0) return;

            cloudsEffect.Render(renderRects, startIndex, length);

            for (int i = startIndex; i < startIndex + length; ++i)
            {
                Render(DstArgs.Surface, SrcArgs.Surface, renderRects[i]);
            }
        }

        private void Render(Surface dst, Surface src, Rectangle rect)
        {
            ColorBgra currentPixel;
            for (int y = rect.Top; y < rect.Bottom; y++)
            {
                if (IsCancelRequested) return;
                for (int x = rect.Left; x < rect.Right; x++)
                {
                    currentPixel = (customColor) ? color : src[x, y];
                    currentPixel.A = Int32Util.ClampToByte(cloudSurface[x, y].A + src[x, y].A - byte.MaxValue);

                    dst[x, y] = currentPixel;
                }
            }
        }

        protected override void OnDispose(bool disposing)
        {
            if (disposing)
            {
                if (emptySurface != null)
                {
                    emptySurface.Dispose();
                    emptySurface = null;
                }

                if (cloudSurface != null)
                {
                    cloudSurface.Dispose();
                    cloudSurface = null;
                }

                if (cloudsEffect != null)
                {
                    cloudsEffect.Dispose();
                    cloudsEffect = null;
                }
            }

            base.OnDispose(disposing);
        }
    }
}
