(
Using('W.Expressions.FuncDefs_DB', 'W.Expr.Sql.dll', 'db::'),
Using('W.Expressions.Sql.FuncDefs_Ora', 'WDbOracle.dll', 'ora::'),
Using('W.Expressions.Sql.FuncDefs_MsSql', 'W.DB.MsSql.dll', 'sql::'),
Using('W.Expressions.FuncDefs_Solver', 'W.Solver.dll', 'solver::'),
Using('Pipe.Exercises.FuncDefs_Pipe', 'PipeExercises.exe', 'pipe::'),
Using('Pipe.Exercises.FuncDefs_PPM', 'PipeExercises.exe', 'pods::'),

let(ppmIdType, 'uniqueidentifier'),
let(ppmStr, 'nvarchar'),
let(ppmTime, 'datetime2(3)'),

// Define quantities
DefineQuantity("ID", "ID", "code"),
DefineQuantity("Code", "code", "string"),
DefineQuantity("Name", "name", "string"),
DefineQuantity("Shortname", "shortname", "string"),
//DefineQuantity("Designator","designator","string"),
DefineQuantity("Descr", "descr", "string"),
DefineQuantity("Comments", "cmnts", "string"),
//DefineQuantity("SPECIFICATION", "spec", "string"),
DefineQuantity("Purpose", "purpose", "string"),
DefineQuantity("Type", "type", "string"),
DefineQuantity("ClCD", "clcd", "string"),
DefineQuantity("GrpClsCD", "grpclscd", "string"),
DefineQuantity("SystemType", "systype", "string"),
DefineQuantity("Diameter", "diam", "mm"),
DefineQuantity("InnerDiam", "inndiam", "mm"),
DefineQuantity("OuterDiam", "outdiam", "mm"),
DefineQuantity("Radius", "radius", "mm"),
DefineQuantity("Thickness", "thickness", "mm"),
DefineQuantity("SequenceNum", "seqnum", "1"),
DefineQuantity("Number", "number", "1"),
DefineQuantity("Factor", "factor", "1"),
DefineQuantity("CurrIndic", "currind", "string"),
DefineQuantity("User", "user", "nvarchar(50)"),
DefineQuantity("Creator", "creator", "string"),
DefineQuantity("Editor", "editor", "string"),
//DefineQuantity("CreaTime", "creatime", "dt"), // creation time
//DefineQuantity("EdiTime", "editime", "dt"), // editing time
//DefineQuantity("InstTime", "insttime", "dt"),
DefineQuantity("XCoord", "xcoord", "1"),
DefineQuantity("YCoord", "ycoord", "1"),
DefineQuantity("ZCoord", "zcoord", "1"),
DefineQuantity("RawGeom", "rawgeom", "bytes"),
DefineQuantity("Geometry", "geometry", "bytes"),
DefineQuantity("Measure", "measure", "m"),
DefineQuantity("Altitude", "altitude", "m"),
DefineQuantity("Money", "money", "1"),
DefineQuantity("Height", "height", "m"),
DefineQuantity("Depth", "depth", "m"),
DefineQuantity("Percent", "perc", "%"),
DefineQuantity("Watercut", "watercut", "%"),
DefineQuantity("Ratio", "ratio", "1"),
DefineQuantity("Width", "width", "1"),
DefineQuantity("Count", "count", "1"),
DefineQuantity("VolRate", "volrate", "m^3/day"),
DefineQuantity("Velocity", "velocity", "m/s"),
DefineQuantity("Comprssblty", "Comprssblty", "1"),
DefineQuantity("GasFactor", "GF", "1"),
DefineQuantity("TimeSpan", "timespan", "day"),
DefineQuantity("Viscosity", "viscosity", "sP"),
DefineQuantity("KinemVisc", "kinevisc", "mm^2/s"),
DefineQuantity("Speed", "speed", "m/s"),
DefineQuantity("RD", "rd", "string"), // symbolic code lookup
DefineQuantity("HRD", "hrd", "string"), // hierarchical  code lookup
DefineQuantity("RC", "rc", "1"), // numeric code lookup

// OIS Pipe Connection
let(oraPipeConn, ora::NewConnection(
	"pipe48/pipe48@pb.ssrv.tk:1521/oralin",
	//"pipe_bashneft/1@pb.ssrv.tk:1521/oralin",
	4, // nOraConnections
	{	"ALTER SESSION SET NLS_TERRITORY = cis"
		, "ALTER SESSION SET CURSOR_SHARING = SIMILAR"
		, "ALTER SESSION SET NLS_NUMERIC_CHARACTERS ='. '"
		, "ALTER SESSION SET NLS_COMP = ANSI"
		, "ALTER SESSION SET NLS_SORT = BINARY"
	}
)..Cached('WExpr:oraPipeConn', 600)),

// WELLOP connection
let(oraWellopConn, ora::NewConnection(
	"WELLOP/WELLOP@pb.ssrv.tk:1522/oralin2",
	4, // nOraConnections
	{ "ALTER SESSION SET NLS_TERRITORY = cis"
		, "ALTER SESSION SET CURSOR_SHARING = SIMILAR"
		, "ALTER SESSION SET NLS_NUMERIC_CHARACTERS ='. '"
		, "ALTER SESSION SET NLS_COMP = ANSI"
		, "ALTER SESSION SET NLS_SORT = BINARY"
	}
)..Cached('WExpr:oraWellopConn', 600)),

// MS SQL Connection
let(sqlConn, sql::NewConnection(
	"geoserver/geo1412@alferovav/PPM.Ugansk.Test",
	4, // nSqlConnections
	{ }
)..Cached('WExpr:SqlConn', 600)),

// Declare data loading functions
//db::UseSqlAsFuncsFrom("Pipe.meta.sql", { 'TimeSlice' }, oraConn, "Pipe"),
//db::UseSqlAsFuncsFrom("PPM.meta.sql", { 'Raw', 'TimeSlice' }, oraConn, 'PPM'),
//db::UseSqlAsFuncsFrom("PPM.meta.sql", { 'TimeSlice' }, sqlConn, 'PPM'),
db::UseSqlAsFuncsFrom("PPM.test.sql", { 'TimeSlice' }, sqlConn, 'PPM'),

//solver::DefineProjectionFuncs({'_CLCD_PIPE','CLASS_DICT_PIPE'}, { '_NAME_PIPE','_SHORTNAME_PIPE' }, data, pipe::GetClassInfo(data) ),
//
//pods::LookupHelperFuncs(sqlConn, { 'pipeline_type_cl', 'PipelineType' }, 'PPM'),
//// 
//
//let(AT_TIME__XT, DATEVALUE('2019-04-17')),
//let(PIPELINE_ID_PIPE, 5059),
//
////solver:FindSolutionExpr({'PIPELINE_ID_PIPE','AT_TIME__XT'}, {'PU_RAWGEOM_PIPE'})
////solver:FindSolutionExpr({ }, { 'CLASS_DICT_PIPE' })
////solver:FindSolutionExpr({ }, { 'PipeIntCoatKind_CLCD_PIPE', 'PipeIntCoatKind_NAME_PIPE', 'PipeNode_NAME_PIPE' })
//solver::FindSolutionExpr({ }, { 'PipelineType_DICT_PPM' })
//	.solver::ExprToExecutable().AtNdx(0)

)