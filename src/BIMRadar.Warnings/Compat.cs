using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Autodesk.Revit.DB;

namespace BIMRadar.Warnings
{
    /// <summary>
    /// The ONE place for Revit-version and runtime differences.
    /// Check every Revit API member against 2022-2027 before adding it here.
    /// </summary>
    internal static class Compat
    {
        /// <summary>Runtime description, e.g. ".NET Framework 4.8.9300.0" or ".NET 8.0.11".</summary>
        public static string RuntimeDescription
        {
            get { return RuntimeInformation.FrameworkDescription; }
        }

        // cached reflection lookups (IdValue / Eid are called thousands of times per run)
        private static readonly System.Reflection.ConstructorInfo CtorLong = typeof(ElementId).GetConstructor(new[] { typeof(long) });
        private static readonly System.Reflection.ConstructorInfo CtorInt = typeof(ElementId).GetConstructor(new[] { typeof(int) });
        private static readonly System.Reflection.PropertyInfo IdProp = typeof(ElementId).GetProperty("Value") ?? typeof(ElementId).GetProperty("IntegerValue");

        // ElementId from a number in every Revit version (2022/2023: int constructor, 2024+: long constructor)
        public static ElementId Eid(long v)
        {
            if (CtorLong != null) return (ElementId)CtorLong.Invoke(new object[] { v });
            return (ElementId)CtorInt.Invoke(new object[] { (int)v });
        }

        // #81: integer value of an ElementId in every Revit version (2024+: Value (long); 2022/2023: the int property). Reflection, no direct member call.
        public static long IdValue(ElementId id)
        {
            if (id == null) return long.MinValue;
            if (IdProp == null) return long.MinValue;
            return Convert.ToInt64(IdProp.GetValue(id, null));
        }
    }
}
