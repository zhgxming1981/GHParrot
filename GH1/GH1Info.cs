using Grasshopper;
using Grasshopper.Kernel;
using System;
using System.Drawing;

namespace NS_Parrot
{
    public class GH1Info : GH_AssemblyInfo
    {
        public override string Name => "GH1";

        //Return a 24x24 pixel bitmap to represent this GHA library.
        public override Bitmap Icon => null;

        //Return a short string describing the purpose of this GHA library.
        public override string Description => "";

        public override Guid Id => new Guid("977F600F-6031-4A42-B800-5EA7CD9A9A6B");

        //Return a string identifying you or your company.
        public override string AuthorName => "";

        //Return a string representing your preferred contact details.
        public override string AuthorContact => "";

    }
}
