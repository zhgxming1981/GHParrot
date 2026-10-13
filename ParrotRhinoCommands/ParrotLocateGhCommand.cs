using Rhino;
using Rhino.Commands;
using Rhino.PlugIns;
using System;
using System.Runtime.InteropServices;

namespace NS_Parrot.RhinoCommands
{
    [Guid("1C5DD8B7-0FD7-4D13-89C4-335F889BBD31")]
    public sealed class ParrotRhinoCommandsPlugIn : PlugIn
    {
        public static ParrotRhinoCommandsPlugIn Instance { get; private set; }

        public ParrotRhinoCommandsPlugIn()
        {
            Instance = this;
        }
    }

    [Guid("07A92399-6FB5-448F-9F0B-2A25B107B064")]
    public sealed class ParrotLocateGhCommand : Command
    {
        public override string EnglishName => "ParrotLocateGh";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            RhinoApp.WriteLine("[ParrotLocateGh] 开始执行。");
            if (RhinoEntityGhLocator.LocateSelectedObject(out string message))
            {
                RhinoApp.WriteLine(message);
                return Result.Success;
            }

            RhinoApp.WriteLine(message);
            return Result.Nothing;
        }
    }
}
