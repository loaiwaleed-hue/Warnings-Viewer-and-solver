using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BIMRadar.Warnings.Standalone
{
    [Transaction(TransactionMode.Manual)]
    public class OpenCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Launcher.Open(commandData.Application);
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class ReviewCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Launcher.Review(commandData.Application);
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class SolveViewCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Launcher.SolveView(commandData.Application);
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class ReportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Launcher.Report(commandData.Application);
            return Result.Succeeded;
        }
    }
}
