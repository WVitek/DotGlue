using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace W.Common
{
    // reference: http://en.wikipedia.org/wiki/List_of_physical_quantities
    // reference: http://en.wikipedia.org/wiki/International_System_of_Units

    /// <summary>
    /// Единица измерения
    /// </summary>
    public interface IDimensionUnit
    {
        string Name { get; }
        string ShortName { get; }
        string Dimension { get; }
    }

    /// <summary>
    /// Физическая величина
    /// </summary>
    public interface IPhysicalQuantity
    {
        string Name { get; }
        string Symbol { get; }
        IDimensionUnit DefaultDimensionUnit { get; }
    }

    public enum Quantity
    {
        // base quantities
        length, mass, time, electric_current,
        temperature, amount_of_substance, luminous_intensity,
        // some derived quantities
        angle, solid_angle, frequency, force, pressure, energy, power, voltage, electric_capacitance, electric_resistance, volume, density
    }

    public static class Quantities
    {
        class Quantity : IPhysicalQuantity
        {
            public string name, symbol;
            public IDimensionUnit defaultUnit;
            public override string ToString() { return Name; }

            public string Name { get { return name; } }
            public string Symbol { get { return symbol; } }
            public IDimensionUnit DefaultDimensionUnit { get { return defaultUnit; } }
        }
        class Unit : IDimensionUnit
        {
            public string name, shortName, dimension;
            public override string ToString() { return Name; }

            public string Name { get { return name; } }
            public string ShortName { get { return shortName; } }
            public string Dimension { get { return dimension; } }
        }

        static object syncRoot = new object();

        static Dictionary<string, IPhysicalQuantity> quantities = new Dictionary<string, IPhysicalQuantity>(StringComparer.OrdinalIgnoreCase);
        static Dictionary<string, IDimensionUnit> units = new Dictionary<string, IDimensionUnit>(StringComparer.OrdinalIgnoreCase);

        static readonly char[] dimensionsSeparators = new char[] { '^', '*', '/' };

        static IDimensionUnit defineUnit(IDimensionUnit u)
        {
            IDimensionUnit ou;
            bool alreadyDefined = units.TryGetValue(u.Name, out ou) || units.TryGetValue(u.ShortName, out ou) || units.TryGetValue(u.Dimension, out ou);
            if (alreadyDefined)
            {
                // redefinition check
                if (string.Compare(ou.Name, u.Name, StringComparison.OrdinalIgnoreCase) == 0 &&
                    string.Compare(ou.Dimension, u.Dimension, StringComparison.OrdinalIgnoreCase) == 0)
                    return ou;
                System.Diagnostics.Trace.Assert(false, $"Redefinition of unit '{u.ShortName}__{u.Dimension}' : {ou.ShortName}__{ou.Dimension}");
            }
            // check unit dimension (all subdimensions must be predefined)
            var subdimensions = u.Dimension.Split(dimensionsSeparators);
            if (subdimensions.Length > 1)
                foreach (var subdim in subdimensions)
                    if (char.IsLetter(subdim[0]))
                        System.Diagnostics.Trace.Assert(units.ContainsKey(subdim), "Unknown dimension '" + subdim + "' in '" + u.Dimension + '\'');
                    else
                    {
                        float tmp;
                        System.Diagnostics.Trace.Assert(float.TryParse(subdim, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out tmp)
                            && Math.Abs(tmp) > float.Epsilon, "Invalid number '" + subdim + "' in dimension expression");
                    }
            units.Add(u.Name, u);
            if (!units.ContainsKey(u.ShortName))
                units.Add(u.ShortName, u);
            if (!units.ContainsKey(u.Dimension))
                units.Add(u.Dimension, u);
            return u;
        }

        /// <param name="unitName">string or enumeration item</param>
        /// <param name="unitShortName">string or enumeration item</param>
        /// <param name="unitDimension">string or enumeration item</param>
        public static IDimensionUnit DefineUnit(object unitName, object unitShortName, object unitDimension)
        {
            lock (syncRoot)
                return defineUnit(newUnit(unitName.ToString(), unitShortName.ToString(), unitDimension.ToString()));
        }

        static IPhysicalQuantity newQuantity(string quantityName, string quantitySymbol, string dimension)
        { return new Quantity() { name = quantityName, symbol = quantitySymbol, defaultUnit = getOrDefineUnit(dimension) }; }

        static IDimensionUnit newUnit(string unitName, string unitShortName, string unitDimension)
        { return new Unit() { name = unitName, shortName = unitShortName, dimension = unitDimension }; }

        static IDimensionUnit getOrDefineUnit(string dimension)
        {
            IDimensionUnit du;
            if (units.TryGetValue(dimension, out du))
                return du;
            else return defineUnit(newUnit(dimension, dimension, dimension));
        }

        public static IDimensionUnit GetUnit(string dimension)
        {
            IDimensionUnit du;
            lock (syncRoot)
                return units.TryGetValue(dimension, out du) ? du : null;
        }

        static void defineQuantities(params IPhysicalQuantity[] lst)
        {
            foreach (var q in lst)
            {
                var qName = q.Name;//.ToLowerInvariant();
                System.Diagnostics.Trace.Assert(!quantities.ContainsKey(qName), "Quantity already defined");
                //defineUnit(q.DefaultDimensionUnit);
                quantities.Add(qName, q);
            }
        }

        public static IPhysicalQuantity DefineQuantity(object quantityName, object quantitySymbol, object quantityDimension)
        {
            lock (syncRoot)
            {
                var name = quantityName.ToString();
                var symbol = quantitySymbol.ToString();
                var dimension = quantityDimension.ToString();
                IPhysicalQuantity q;
                var qName = name;//.ToLowerInvariant();
                var qSymb = symbol;//.ToLowerInvariant();
                if (quantities.TryGetValue(qName, out q) || quantities.TryGetValue(qSymb, out q))
                {
                    var u = getOrDefineUnit(dimension);
                    System.Diagnostics.Trace.Assert(
                        name == q.Name && symbol == q.Symbol && object.ReferenceEquals(u, q.DefaultDimensionUnit),
                        $"Different redefinition of quantity '{q.Name}' : '{name}'"
                    );
                }
                else
                {
                    q = newQuantity(name, symbol, dimension);
                    quantities.Add(qName, q);
                    //if (qName != qSymb)
                    //	quantities.Add(qSymb, q);
                }
                return q;
            }

        }

        static void defineUnits(params IDimensionUnit[] lst)
        {
            foreach (var u in lst) defineUnit(u);
        }

        /// <summary>
        /// Declares base quantities and units
        /// </summary>
        static Quantities()
        {
            defineUnits(
                newUnit("metre", "m", "m"),
                newUnit("kilogram", "kg", "kg"),
                newUnit("second", "s", "s"),
                newUnit("ampere", "A", "A"),
                newUnit("kelvin", "°K", "K"),
                newUnit("mole", "mol", "mol"),
                newUnit("candela", "cd", "cd"),
                newUnit("radian", "rad", "m/m"),
                newUnit("steradian", "sr", "m^2/m^2"),
                newUnit("hertz", "Hz", "1/s"),
                newUnit("newton", "N", "kg*m*s^-2"),
                newUnit("pascal", "Pa", "kg*m^-1*s^-2"),
                newUnit("joule", "J", "kg*m^2*s^-2"),
                newUnit("watt", "W", "kg*m^2*s^-3"),
                newUnit("volt", "V", "kg*m^2*A^-1*s^-3"),
                newUnit("farad", "F", "kg^-1*m^-2*s^4*A^2"),
                newUnit("ohm", "Ω", "kg*m^2*A^-2*s^-3"),
                newUnit("atm", "atm", "101325*Pa")
            );
            defineUnits(
                newUnit("String", "Str", "str"),
                newUnit("Code", "Code", "code"),
                newUnit("Minute", "Min", "60*s"),
                newUnit("Ton", "Ton", "1000*kg"),
                newUnit("DateTime", "DT", "dt"),
                newUnit("ExcelTime", "XT", "xt"), // XT = eXcel Time
                newUnit("Celsius", "C", "C"),
                newUnit("SqM", "SqM", "m^2"),  // SQM = sq m = square meter
                newUnit("SqMM", "SqMM", "m^2*1e-6"),  // SQMM = sq mm = square millimeter
                newUnit("TpCM", "TpCM", "ton/m^3"),  // TPCM = ton per cubic meter
                newUnit("KGpCM", "KGpCM", "kg/m^3"),  // KgPCM = kilogram per cubic meter
                newUnit("Gram", "g", "0.001*kg"),
                newUnit("GpCM", "GpCM", "g/m^3"),  // GPCM = gram per cubic meter
                newUnit("Millimeter", "MM", "0.001*m"),
                newUnit("TimesPerMinute", "TpM", "1/min"),
                newUnit("stdg", "stdg", "9.80665*m*s^-2"),
                newUnit("VolPercent", "%vol", "m^3/m^3*100"),
                newUnit("KiloWatt", "KW", "1000*W"),
                newUnit("Day", "Day", "86400*s")
            );
            defineQuantities(
                newQuantity("Length", "l", "m"),
                newQuantity("Mass", "m", "kg"),
                newQuantity("TIME", "t", "dt"),
                newQuantity("Current", "I", "A"),
                newQuantity("Temperature", "T", "C"),
                newQuantity("AmountOfSubstance", "n", "mol"),
                newQuantity("LuminousIntensity", "L", "cd"),
                // derived quantities and units
                newQuantity("Angle", "θ", "rad"),
                newQuantity("SolidAngle", "Ω", "sr"),
                newQuantity("Frequency", "f", "Hz"),
                newQuantity("Force", "F", "N"),
                newQuantity("Pressure", "p", "Pa"),
                newQuantity("Energy", "E", "J"),
                newQuantity("Power", "P", "W"),
                newQuantity("Voltage", "V", "V"),
                newQuantity("Capacitance", "C", "F"),
                newQuantity("Resistance", "R", "Ω"),
                newQuantity("Volume", "V", "m^3"),
                newQuantity("Area", "A", "m^2"),
                newQuantity("Density", "ρ", "kg*m^-3"),
                // for general purpose values
                newQuantity("Obj", "obj", "object")
            );
        }

        public static IPhysicalQuantity GetQuantity(string quantityName, bool canRetNull = false)
        {
            lock (syncRoot)
            {
                if (quantities.TryGetValue(quantityName, out var q))
                    return q;
                if (canRetNull)
                    return null;
                throw new KeyNotFoundException("Undefined quantity '" + quantityName + '\'');
            }
        }

        public static bool IsQuantity(string quantityName) { lock (syncRoot) return quantities.ContainsKey(quantityName); }
        public static bool IsDimension(string dimension) { lock (syncRoot) return units.ContainsKey(dimension); }

        public static IDimensionUnit GetOrDefineUnit(string dimension) { lock (syncRoot) return getOrDefineUnit(dimension); }
    }

    public class ValueInfo : IEqualityComparer<ValueInfo>
    {
        public readonly IPhysicalQuantity quantity;
        public readonly IDimensionUnit unit;
        public readonly string substance;
        public readonly string location;

        public enum Part
        {
            Substance = 0,
            Quantity = 1,
            Location = 2,
            Unit = 3
        }

        private ValueInfo(IPhysicalQuantity quantity, IDimensionUnit unit, string substance, string location)
        {
            this.quantity = quantity;
            this.unit = unit;
            this.substance = substance;
            this.location = location;
        }

        public static ValueInfo Create(string quantityName, string dimension, string substance, string location, bool mayReturnNull = false)
        {
            var quantity = Quantities.GetQuantity(quantityName, mayReturnNull);
            if (quantity == null)
                return null;
            var unit = (dimension == null) ? quantity.DefaultDimensionUnit : Quantities.GetOrDefineUnit(dimension);
            return new ValueInfo(quantity, unit, substance, location);
        }

        /// <param name="descriptor">string in format: "substance_quantity_location_dimension", where location, dimension and appropriated underscore characters is optional</param>
        public static ValueInfo Create(string descriptor, bool mayReturnNull = false, string defaultLocation = null)
        {
            var parts = descriptor.Split('_');
            if (parts.Length < 2 || 4 < parts.Length || parts[0].Length == 0 || parts[1].Length == 0)
                if (mayReturnNull)
                    return null;
                else throw new ArgumentException("Wrong descriptor format: " + descriptor, "descriptor");
            return Create(
                // quantity
                parts[1],
                // dimension
                (parts.Length > 3) ? parts[3] : null,
                // substance
                parts[0],
                // location
                (parts.Length > 2 && parts[2].Length > 0) ? parts[2] : defaultLocation ?? string.Empty,
                // 
                mayReturnNull
            );
        }

        public static string OverrideByMask(string descriptor, string mask)
        {
            var d4 = FourParts(descriptor);
            var m4 = FourParts(mask);
            for (int i = 0; i < 4; i++)
                d4[i] = m4[i] ?? d4[i];
            return FromParts(d4);
        }

        public static string[] FourParts(string descriptor)
        {
            var p = descriptor.Split('_');
            var r = new string[4];
            int n = Math.Min(4, p.Length);
            for (int i = 0; i < n; i++)
                r[i] = string.IsNullOrEmpty(p[i]) ? null : p[i];
            return r;
        }

        public static string FromParts(string[] fourParts)
        {
            int k = fourParts.Length - 1;
            for (; k > 1 && fourParts[k] == null; k--) ;
            var sb = new System.Text.StringBuilder(30);
            for (int i = 0; i <= k; i++)
            {
                if (i > 0)
                    sb.Append('_');
                sb.Append(fourParts[i]);
            }
            return sb.ToString();
        }

        public static string WithoutParts(string descriptor, params Part[] parts)
        {
            var items = descriptor.Split('_');
            foreach(var p in parts)
            {
                int i = (int)p;
                if (i < items.Length)
                    items[i] = null;
            }
            return FromParts(items);
        }

        static int Specificity(string descriptor)
        {
            int n = 0;
            int L = descriptor.Length;
            int i = 0;
            while (i < L)
            {
                int j = descriptor.IndexOf('_', i + 1);
                if (j < 0)
                    j = L;
                if (j - i > 1)
                    n++;
                i = j;
            }
            return n;
        }

        public static string MostSpecific(string descriptorA, string descriptorB)
        {
            int specA = Specificity(descriptorA);
            int specB = Specificity(descriptorB);
            return (specA >= specB) ? descriptorA : descriptorB;
        }

        public static bool IsID(string s)
        {
            return s.IndexOf("_ID_", StringComparison.OrdinalIgnoreCase) > 0
                || s.EndsWith("_ID", StringComparison.OrdinalIgnoreCase);
        }

        public sealed class At_TIME__XT { }
        public sealed class A_TIME__XT { }
        public sealed class B_TIME__XT { }

        /// <summary>
        /// Checks that parameter is one of reserved names used definition of a time span or time slice
        /// </summary>
        public static bool IsTimeKeyword(string s)
        {
            if (string.Compare(s, nameof(At_TIME__XT), StringComparison.OrdinalIgnoreCase) == 0)
                return true;
            if (string.Compare(s, nameof(A_TIME__XT), StringComparison.OrdinalIgnoreCase) == 0)
                return true;
            if (string.Compare(s, nameof(B_TIME__XT), StringComparison.OrdinalIgnoreCase) == 0)
                return true;
            return false;
        }

        public static bool IsDescriptor(string s)
        {
            var p = s.Split('_');
            return 2 <= p.Length && p.Length <= 4 && Quantities.IsQuantity(p[1]) && (p.Length < 4 || Quantities.IsDimension(p[3]));
        }

        /// <summary>
        /// Create ValueInfo[] from descriptors
        /// </summary>
        /// <param name="descriptors">strings in format: "substance_quantity_location_dimension", where location, dimension and appropriated underscore characters is optional</param>
        public static ValueInfo[] CreateMany(params string[] descriptors)
        {
            var res = new ValueInfo[descriptors.Length];
            for (int i = 0; i < descriptors.Length; i++)
                res[i] = ValueInfo.Create(descriptors[i]);
            return res;
        }

        ///// <summary>
        ///// Create ValueInfo[] from descriptors with default location specified (for descriptors without location)
        ///// </summary>
        //public static ValueInfo[] CreateManyInLocation(string Location, params string[] descriptors)
        //{
        //    var res = new ValueInfo[descriptors.Length];
        //    for (int i = 0; i < descriptors.Length; i++)
        //        res[i] = ValueInfo.Create(descriptors[i], defaultLocation: Location);
        //    return res;
        //}

        public override string ToString()
        {
            string dimension;
            if (unit == quantity.DefaultDimensionUnit)
                dimension = null;
            else dimension = unit.ShortName;
            var sb = new System.Text.StringBuilder(substance + '_' + quantity.Name);
            if (dimension != null)
            {
                sb.Append('_');
                if (!string.IsNullOrEmpty(location))
                    sb.Append(location);
                sb.Append('_').Append(dimension);
            }
            else if (!string.IsNullOrEmpty(location))
                sb.Append('_').Append(location);
            return sb.ToString();//.ToUpperInvariant();
        }

        public string[] Parts()
        {
            return new string[4] {
                substance,//.ToUpperInvariant(),
                quantity.Name,//.ToUpperInvariant(),
                string.IsNullOrEmpty(location) ? null : location,//.ToUpperInvariant(),
                (unit == quantity.DefaultDimensionUnit) ? null : unit.ShortName//.ToUpperInvariant()
            };
        }

        public int DescriptorLength()
        {
            int n = substance.Length + 1 + quantity.Name.Length;
            int nLoc = string.IsNullOrEmpty(location) ? 0 : location.Length;
            if (unit == quantity.DefaultDimensionUnit)
                return (nLoc == 0) ? n : n + 1 + nLoc;
            return n + 1 + nLoc + 1 + unit.ShortName.Length;
        }

        public bool Equals(ValueInfo x, ValueInfo y)
        {
            return x.quantity == y.quantity && x.substance == y.substance && x.location == y.location && x.unit == y.unit;
        }

        public int GetHashCode(ValueInfo obj)
        {
            return obj.GetHashCode();
        }

        public override int GetHashCode()
        {
            return substance.GetHashCode() ^ quantity.Name.GetHashCode() ^ location.GetHashCode() ^ unit.Name.GetHashCode();
        }

        public static int CompatibleCompare(ValueInfo a, ValueInfo b)
        {
            int i = string.Compare(a.substance, b.substance, StringComparison.InvariantCultureIgnoreCase);
            if (i != 0)
                return i;
            if (a.quantity != b.quantity)
                return string.Compare(a.quantity.Name, b.quantity.Name, StringComparison.InvariantCultureIgnoreCase);
            if (a.unit != b.unit)
                return string.Compare(a.unit.Name, b.unit.Name, StringComparison.InvariantCultureIgnoreCase);
            return 0;
        }

        public static int DescriptorsCompatibleCompare(string a, string b)
        {
            if (string.Compare(a, b, StringComparison.InvariantCultureIgnoreCase) == 0)
                return 0;
            var va = Create(a);
            var vb = Create(b);
            return CompatibleCompare(va, vb);
        }

        public static readonly IEqualityComparer<ValueInfo> CompatibilityComparer = new CompatibilityComparerImpl();
        public static readonly ValueInfo[] Empties = new ValueInfo[0];

        class CompatibilityComparerImpl : IEqualityComparer<ValueInfo>
        {
            public bool Equals(ValueInfo x, ValueInfo y)
            {
                return CompatibleCompare(x, y) == 0;
            }

            public int GetHashCode(ValueInfo obj)
            {
                return obj.GetHashCode();
            }
        }
    }

    public abstract class ValueInfoAttribute : Attribute
    {
        public readonly ValueInfo info;
        public ValueInfoAttribute(object quantityName, object dimension, object substance, object location)
        {
            this.info = ValueInfo.Create(quantityName.ToString(), dimension.ToString(), substance.ToString(), location.ToString());
        }
        private ValueInfoAttribute() { }
        protected ValueInfoAttribute(string descriptor) { info = ValueInfo.Create(descriptor); }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class ArgumentInfoAttribute : ValueInfoAttribute
    {
        public readonly int index;
        //public ArgumentInfoAttribute(object quantityName, object dimension, object substance, object location) :
        //	base(quantityName, dimension, substance, location) { }
        public ArgumentInfoAttribute(int index, string descriptor) : base(descriptor) { this.index = index; }
    }

    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
    public class ParameterInfoAttribute : ValueInfoAttribute
    {
        public ParameterInfoAttribute(object quantityName, object dimension, object substance, object location) :
            base(quantityName, dimension, substance, location)
        { }
        public ParameterInfoAttribute(string descriptor) : base(descriptor) { }
    }

    [AttributeUsage(AttributeTargets.ReturnValue, AllowMultiple = true)]
    public class ResultInfoAttribute : ValueInfoAttribute
    {
        public readonly int index;
        public ResultInfoAttribute(int index, string quantityName, string dimension, string substance, string location) :
            base(quantityName, dimension, substance, location)
        { this.index = index; }
        public ResultInfoAttribute(int index, string descriptor) : base(descriptor) { this.index = index; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class DefineUnitsAttribute : Attribute
    {
        public DefineUnitsAttribute(params object[] unitsInfo)
        {
            int n = unitsInfo.Length;
            System.Diagnostics.Trace.Assert(n % 3 == 0);
            for (int i = 0; i < n; i += 3)
                Quantities.DefineUnit(unitsInfo[i], unitsInfo[i + 1], unitsInfo[i + 2]);
        }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class DefineQuantitiesAttribute : Attribute
    {
        public DefineQuantitiesAttribute(params object[] quantitiesInfo)
        {
            int n = quantitiesInfo.Length;
            System.Diagnostics.Trace.Assert(n % 3 == 0);
            for (int i = 0; i < n; i += 3)
                Quantities.DefineQuantity(quantitiesInfo[i], quantitiesInfo[i + 1], quantitiesInfo[i + 2]);
        }
    }
}