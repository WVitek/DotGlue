using System;

namespace Pipe.Exercises
{
    using GeoAPI.CoordinateSystems;
    using ProjNet.CoordinateSystems;
    using ProjNet.CoordinateSystems.Transformations;

    public static class GeoCoordConv
    {
        public const string PROJCS_MSK02_1 = "PROJCS[\"МСК-02 зона 1\",GEOGCS[\"Pulkovo 1942 (2008)\",DATUM[\"Pulkovo 1942 (2008)\",SPHEROID[\"Krassowsky 1940\", 6378245.0, 298.3],TOWGS84[23.57,-140.95,-79.8,0,-0.35,-0.79,-0.22]],PRIMEM[\"Greenwich\", 0.0],UNIT[\"degree\", 0.017453292519943295],AXIS[\"Longitude\", EAST],AXIS[\"Latitude\", NORTH]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"central_meridian\",55.03333333333],PARAMETER[\"latitude_of_origin\",0],PARAMETER[\"scale_factor\",1],PARAMETER[\"false_easting\",1300000],PARAMETER[\"false_northing\",-5409414.70],UNIT[\"m\", 1.0],AXIS[\"x\", EAST],AXIS[\"y\", NORTH],AUTHORITY[\"EPSG\",\"5044021\"]]";
        public const string PROJCS_MSK02_2 = "PROJCS[\"МСК-02 зона 2\",GEOGCS[\"Pulkovo 1942 (2008)\",DATUM[\"Pulkovo 1942 (2008)\",SPHEROID[\"Krassowsky 1940\", 6378245.0, 298.3],TOWGS84[23.57,-140.95,-79.8,0,-0.35,-0.79,-0.22]],PRIMEM[\"Greenwich\", 0.0],UNIT[\"degree\", 0.017453292519943295],AXIS[\"Longitude\", EAST],AXIS[\"Latitude\", NORTH]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"central_meridian\",58.03333333333],PARAMETER[\"latitude_of_origin\",0],PARAMETER[\"scale_factor\",1],PARAMETER[\"false_easting\",2300000],PARAMETER[\"false_northing\",-5409414.70],UNIT[\"m\", 1.0],AXIS[\"x\", EAST],AXIS[\"y\", NORTH],AUTHORITY[\"EPSG\",\"5044022\"]]";

        public const string PROJCS_MSK83_5 = "PROJCS[\"МСК-83 зона 5\",GEOGCS[\"Pulkovo 1942 (2008)\",DATUM[\"Pulkovo 1942 (2008)\",SPHEROID[\"Krassowsky 1940\", 6378245.0, 298.3],TOWGS84[23.57,-140.95,-79.8,0,-0.35,-0.79,-0.22]],PRIMEM[\"Greenwich\", 0.0],UNIT[\"degree\", 0.017453292519943295],AXIS[\"Longitude\", EAST],AXIS[\"Latitude\", NORTH]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"central_meridian\",56.03333333333],PARAMETER[\"latitude_of_origin\",0],PARAMETER[\"scale_factor\",1],PARAMETER[\"false_easting\",5400000],PARAMETER[\"false_northing\",-6511057.628],UNIT[\"m\", 1.0],AXIS[\"x\", EAST],AXIS[\"y\", NORTH],AUTHORITY[\"EPSG\",\"5044835\"]]";

        public const string PROJCS_MSK86_1 = "PROJCS[\"МСК-86 зона 1\",GEOGCS[\"Pulkovo 1942 (2008)\",DATUM[\"Pulkovo 1942 (2008)\",SPHEROID[\"Krassowsky 1940\", 6378245.0, 298.3],TOWGS84[23.57,-140.95,-79.8,0,-0.35,-0.79,-0.22]],PRIMEM[\"Greenwich\", 0.0],UNIT[\"degree\", 0.017453292519943295],AXIS[\"Longitude\", EAST],AXIS[\"Latitude\", NORTH]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"central_meridian\",60.05],PARAMETER[\"latitude_of_origin\",0],PARAMETER[\"scale_factor\",1],PARAMETER[\"false_easting\",1500000],PARAMETER[\"false_northing\",-5811057.63],UNIT[\"m\", 1.0],AXIS[\"x\", EAST],AXIS[\"y\", NORTH],AUTHORITY[\"EPSG\",\"5044861\"]]";
        public const string PROJCS_MSK86_2 = "PROJCS[\"МСК-86 зона 2\",GEOGCS[\"Pulkovo 1942 (2008)\",DATUM[\"Pulkovo 1942 (2008)\",SPHEROID[\"Krassowsky 1940\", 6378245.0, 298.3],TOWGS84[23.57,-140.95,-79.8,0,-0.35,-0.79,-0.22]],PRIMEM[\"Greenwich\", 0.0],UNIT[\"degree\", 0.017453292519943295],AXIS[\"Longitude\", EAST],AXIS[\"Latitude\", NORTH]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"central_meridian\",66.05],PARAMETER[\"latitude_of_origin\",0],PARAMETER[\"scale_factor\",1],PARAMETER[\"false_easting\",2500000],PARAMETER[\"false_northing\",-5811057.63],UNIT[\"m\", 1.0],AXIS[\"x\", EAST],AXIS[\"y\", NORTH],AUTHORITY[\"EPSG\",\"5044862\"]]";
        public const string PROJCS_MSK86_3 = "PROJCS[\"МСК-86 зона 3\",GEOGCS[\"Pulkovo 1942 (2008)\",DATUM[\"Pulkovo 1942 (2008)\",SPHEROID[\"Krassowsky 1940\", 6378245.0, 298.3],TOWGS84[23.57,-140.95,-79.8,0,-0.35,-0.79,-0.22]],PRIMEM[\"Greenwich\", 0.0],UNIT[\"degree\", 0.017453292519943295],AXIS[\"Longitude\", EAST],AXIS[\"Latitude\", NORTH]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"central_meridian\",72.05],PARAMETER[\"latitude_of_origin\",0],PARAMETER[\"scale_factor\",1],PARAMETER[\"false_easting\",3500000],PARAMETER[\"false_northing\",-5811057.63],UNIT[\"m\", 1.0],AXIS[\"x\", EAST],AXIS[\"y\", NORTH],AUTHORITY[\"EPSG\",\"5044863\"]]";
        public const string PROJCS_MSK86_4 = "PROJCS[\"МСК-86 зона 4\",GEOGCS[\"Pulkovo 1942 (2008)\",DATUM[\"Pulkovo 1942 (2008)\",SPHEROID[\"Krassowsky 1940\", 6378245.0, 298.3],TOWGS84[23.57,-140.95,-79.8,0,-0.35,-0.79,-0.22]],PRIMEM[\"Greenwich\", 0.0],UNIT[\"degree\", 0.017453292519943295],AXIS[\"Longitude\", EAST],AXIS[\"Latitude\", NORTH]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"central_meridian\",78.05],PARAMETER[\"latitude_of_origin\",0],PARAMETER[\"scale_factor\",1],PARAMETER[\"false_easting\",4500000],PARAMETER[\"false_northing\",-5811057.63],UNIT[\"m\", 1.0],AXIS[\"x\", EAST],AXIS[\"y\", NORTH],AUTHORITY[\"EPSG\",\"5044864\"]]";
        public const string PROJCS_MSK86_5 = "PROJCS[\"МСК-86 зона 5\",GEOGCS[\"Pulkovo 1942 (2008)\",DATUM[\"Pulkovo 1942 (2008)\",SPHEROID[\"Krassowsky 1940\", 6378245.0, 298.3],TOWGS84[23.57,-140.95,-79.8,0,-0.35,-0.79,-0.22]],PRIMEM[\"Greenwich\", 0.0],UNIT[\"degree\", 0.017453292519943295],AXIS[\"Longitude\", EAST],AXIS[\"Latitude\", NORTH]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"central_meridian\",84.05],PARAMETER[\"latitude_of_origin\",0],PARAMETER[\"scale_factor\",1],PARAMETER[\"false_easting\",5500000],PARAMETER[\"false_northing\",-5811057.63],UNIT[\"m\", 1.0],AXIS[\"x\", EAST],AXIS[\"y\", NORTH],AUTHORITY[\"EPSG\",\"5044865\"]]";

        static CoordinateTransformationFactory ctf = new CoordinateTransformationFactory();

        public static Func<double[], double[]> GetConvToWGS84(string fromPROJCS)
        {
            var pcs = (IProjectedCoordinateSystem)ProjNet.Converters.WellKnownText.CoordinateSystemWktReader.Parse(fromPROJCS);
            var mt = ctf.CreateFromCoordinateSystems(pcs, GeographicCoordinateSystem.WGS84).MathTransform;
            return srcP => mt.Transform(srcP);
        }

        public static Func<double[], double[]> GetConvFromWGS84(string fromPROJCS)
        {
            var pcs = (IProjectedCoordinateSystem)ProjNet.Converters.WellKnownText.CoordinateSystemWktReader.Parse(fromPROJCS);
            var mt = ctf.CreateFromCoordinateSystems(GeographicCoordinateSystem.WGS84, pcs).MathTransform;
            return srcP => mt.Transform(srcP);
        }

        static readonly Func<double[], double[]> Conv_MSK_02_1 = GetConvToWGS84(PROJCS_MSK02_1);
        static readonly Func<double[], double[]> Conv_MSK_83_5 = GetConvToWGS84(PROJCS_MSK83_5);
        static readonly Func<double[], double[]> Conv_MSK_86_3 = GetConvToWGS84(PROJCS_MSK86_3);

        static readonly Func<double[], double[]> Conv_UNG = c =>
        {
            var p = new double[c.Length];
            p[0] = c[0] + 3157817.641 + 4330 - 50 - 183;
            p[1] = c[1] - 5810365.348 - 2730 + 264 - 167;
            if (c.Length > 2)
                p[2] = c[2];
            return Conv_MSK_86_3(p);
        };

        public static (Func<double[], double[]> conv, int id) GetConvByOrganization(string PR)
        {
            switch (PR)
            {
                case "НГДУ Арланнефть":
                case "НГДУ Ишимбайнефть":
                case "НГДУ Туймазанефть":
                case "НГДУ Чекмагушнефть":
                case "НГДУ Уфанефть":
                case "УПСНГ":
                    return (Conv_MSK_02_1, 1);
                case "ООО \"Башнефть-Полюс\"":
                    return (Conv_MSK_83_5, 2);
                case "ООО \"Соровскнефть\"":
                    return (Conv_MSK_86_3, 3);
                case "Правдинский РЕГИОН":
                case "Приобский РЕГИОН":
                case "Нефтеюганский РЕГИОН":
                case "Майский РЕГИОН":
                case "ООО \"РН - Юганскнефтегаз\"":
                    return (Conv_UNG, 4);
            }
            return (null, 0);
        }
    }
}
