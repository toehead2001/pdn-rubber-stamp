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
        public string Author => ((AssemblyCopyrightAttribute)base.GetType().Assembly.GetCustomAttributes(typeof(AssemblyCopyrightAttribute), false)[0]).Copyright;
        public string Copyright => ((AssemblyDescriptionAttribute)base.GetType().Assembly.GetCustomAttributes(typeof(AssemblyDescriptionAttribute), false)[0]).Description;
        public string DisplayName => ((AssemblyProductAttribute)base.GetType().Assembly.GetCustomAttributes(typeof(AssemblyProductAttribute), false)[0]).Product;
        public Version Version => base.GetType().Assembly.GetName().Version;
        public Uri WebsiteUri => new Uri("https://forums.getpaint.net/index.php?showtopic=111225");
    }

    [PluginSupportInfo(typeof(PluginSupportInfo), DisplayName = "Rubber Stamp")]
    public class RubberStampEffectPlugin : PropertyBasedEffect
    {
        private int Amount1 = 50; // [2,1000] Scale
        private double Amount2 = 1.0; // [0,1] Roughness
        private int Amount3 = 0; // [255] Reseed
        private ColorBgra Amount4 = ColorBgra.Black; // Color
        private int Amount5 = 85;
        private bool Amount6 = false;

        private readonly BinaryPixelOp normalOp = LayerBlendModeUtil.CreateCompositionOp(LayerBlendMode.Normal);
        private readonly CloudsEffect cloudsEffect = new CloudsEffect();
        private PropertyCollection cloudsProps;
        private Surface emptySurface;
        private Surface cloudSurface;


        private const string StaticName = "Rubber Stamp";
        private static Image StaticIcon => new Bitmap(typeof(RubberStampEffectPlugin), "RubberStamp.png");
        private const string StaticMenu = "Object";
        
        public RubberStampEffectPlugin()
            : base(StaticName, StaticIcon, StaticMenu, EffectFlags.Configurable)
        {
            instanceSeed = unchecked((int)DateTime.Now.Ticks);
        }

        private enum PropertyNames
        {
            Amount1,
            Amount2,
            Amount3,
            Amount4,
            Amount5,
            Amount6
        }

        [ThreadStatic]
        private static Random RandomNumber;

        private int randomSeed;
        private int instanceSeed;


        protected override PropertyCollection OnCreatePropertyCollection()
        {
            List<Property> props = new List<Property>();

            ColorBgra PrimaryColor = EnvironmentParameters.PrimaryColor;
            PrimaryColor.A = 255;

            props.Add(new Int32Property(PropertyNames.Amount1, 50, 3, 100));
            props.Add(new DoubleProperty(PropertyNames.Amount2, 1.0, 0, 1.0));
            props.Add(new Int32Property(PropertyNames.Amount5, 85, byte.MinValue, byte.MaxValue));
            props.Add(new Int32Property(PropertyNames.Amount3, 0, 0, 255));
            props.Add(new BooleanProperty(PropertyNames.Amount6, false));
            props.Add(new Int32Property(PropertyNames.Amount4, ColorBgra.ToOpaqueInt32(PrimaryColor), 0, 0xffffff));

            List<PropertyCollectionRule> propRules = new List<PropertyCollectionRule>();
            propRules.Add(new ReadOnlyBoundToBooleanRule(PropertyNames.Amount4, PropertyNames.Amount6, true));

            return new PropertyCollection(props, propRules);
        }

        protected override ControlInfo OnCreateConfigUI(PropertyCollection props)
        {
            ControlInfo configUI = CreateDefaultConfigUI(props);

            configUI.SetPropertyControlValue(PropertyNames.Amount1, ControlInfoPropertyNames.DisplayName, "Scale");
            configUI.SetPropertyControlValue(PropertyNames.Amount2, ControlInfoPropertyNames.DisplayName, "Roughness");
            configUI.SetPropertyControlValue(PropertyNames.Amount2, ControlInfoPropertyNames.SliderLargeChange, 0.25);
            configUI.SetPropertyControlValue(PropertyNames.Amount2, ControlInfoPropertyNames.SliderSmallChange, 0.05);
            configUI.SetPropertyControlValue(PropertyNames.Amount2, ControlInfoPropertyNames.UpDownIncrement, 0.01);
            configUI.SetPropertyControlValue(PropertyNames.Amount2, ControlInfoPropertyNames.DecimalPlaces, 3);
            configUI.SetPropertyControlValue(PropertyNames.Amount3, ControlInfoPropertyNames.DisplayName, string.Empty);
            configUI.SetPropertyControlType(PropertyNames.Amount3, PropertyControlType.IncrementButton);
            configUI.SetPropertyControlValue(PropertyNames.Amount3, ControlInfoPropertyNames.ButtonText, "Reseed");
            configUI.SetPropertyControlValue(PropertyNames.Amount4, ControlInfoPropertyNames.DisplayName, string.Empty);
            configUI.SetPropertyControlType(PropertyNames.Amount4, PropertyControlType.ColorWheel);
            configUI.SetPropertyControlValue(PropertyNames.Amount5, ControlInfoPropertyNames.DisplayName, "Minimum Opacity");
            configUI.SetPropertyControlValue(PropertyNames.Amount5, ControlInfoPropertyNames.ControlColors, new ColorBgra[] { ColorBgra.White, ColorBgra.Black });
            configUI.SetPropertyControlValue(PropertyNames.Amount6, ControlInfoPropertyNames.DisplayName, "Color");
            configUI.SetPropertyControlValue(PropertyNames.Amount6, ControlInfoPropertyNames.Description, "Use Custom Color");


            return configUI;
        }

        protected override void OnSetRenderInfo(PropertyBasedEffectConfigToken newToken, RenderArgs dstArgs, RenderArgs srcArgs)
        {
            Amount1 = newToken.GetProperty<Int32Property>(PropertyNames.Amount1).Value;
            Amount2 = newToken.GetProperty<DoubleProperty>(PropertyNames.Amount2).Value;
            Amount3 = (byte)newToken.GetProperty<Int32Property>(PropertyNames.Amount3).Value;
            randomSeed = Amount3;
            Amount4 = ColorBgra.FromOpaqueInt32(newToken.GetProperty<Int32Property>(PropertyNames.Amount4).Value);
            Amount5 = newToken.GetProperty<Int32Property>(PropertyNames.Amount5).Value;
            Amount6 = newToken.GetProperty<BooleanProperty>(PropertyNames.Amount6).Value;


            if (emptySurface == null)
                emptySurface = new Surface(srcArgs.Size);
            if (cloudSurface == null)
                cloudSurface = new Surface(srcArgs.Size);

            // Call the Render Clouds function
            cloudsProps = cloudsEffect.CreatePropertyCollection();
            PropertyBasedEffectConfigToken CloudsParameters = new PropertyBasedEffectConfigToken(cloudsProps);
            CloudsParameters.SetPropertyValue(CloudsEffect.PropertyNames.Scale, Amount1);
            CloudsParameters.SetPropertyValue(CloudsEffect.PropertyNames.Power, Amount2);
            CloudsParameters.SetPropertyValue(CloudsEffect.PropertyNames.Seed, Amount3);
            using (EffectEnvironmentParameters environParameters = new EffectEnvironmentParameters(ColorBgra.Black, Color.FromArgb(Amount5, Color.Black), 0, EnvironmentParameters.GetSelection(srcArgs.Bounds), emptySurface))
                cloudsEffect.EnvironmentParameters = environParameters;
            cloudsEffect.SetRenderInfo(CloudsParameters, new RenderArgs(cloudSurface), new RenderArgs(emptySurface));


            base.OnSetRenderInfo(newToken, dstArgs, srcArgs);
        }

        protected override void OnRender(Rectangle[] renderRects, int startIndex, int length)
        {
            if (length == 0) return;
            RandomNumber = GetRandomNumberGenerator(renderRects, startIndex);
            for (int i = startIndex; i < startIndex + length; ++i)
            {
                Render(DstArgs.Surface, SrcArgs.Surface, renderRects[i]);
            }
        }

        private Random GetRandomNumberGenerator(Rectangle[] rois, int startIndex)
        {
            Rectangle roi = rois[startIndex];
            return new Random(instanceSeed ^ (randomSeed << 16) ^ (roi.X << 8) ^ roi.Y);
        }

        private void Render(Surface dst, Surface src, Rectangle rect)
        {
            cloudsEffect.Render(new Rectangle[1] { rect }, 0, 1);

            ColorBgra currentPixel;
            for (int y = rect.Top; y < rect.Bottom; y++)
            {
                if (IsCancelRequested) return;
                for (int x = rect.Left; x < rect.Right; x++)
                {
                    currentPixel = (Amount6) ? Amount4 : src[x, y];
                    currentPixel.A = Int32Util.ClampToByte(cloudSurface[x, y].A + src[x, y].A - byte.MaxValue);

                    dst[x, y] = currentPixel;
                }
            }
        }
    }
}
