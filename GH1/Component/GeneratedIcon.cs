using System.Drawing;

namespace NS_Parrot
{
    internal static class GeneratedIcon
    {
        public static Bitmap Get(string name)
        {
            return parrot.Properties.Resources.ResourceManager.GetObject(name) as Bitmap;
        }
    }
}
