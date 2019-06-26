(
Using('W.Expressions.FuncDefs_DB', 'WExprSql.dll', 'db::'),
Using('W.Expressions.Sql.FuncDefs_Ora', 'WDbOracle.dll', 'ora::'),
Using('W.Expressions.Sql.FuncDefs_MsSql', 'WDbMsSql.dll', 'sql::'),
Using('W.Expressions.FuncDefs_Solver', 'WSolver.dll', 'solver::'),
Using('Pipe.Exercises.FuncDefs_Pipe', 'PipeExcercises.exe', 'pipe::'),
Using('Pipe.Exercises.FuncDefs_PPM', 'PipeExcercises.exe', 'pods::'),

let(ppmIdType, 'uniqueidentifier'),
let(ppmStr, 'nvarchar'),

// Define quantities
DefineQuantity("ID", "ID", ppmIdType),
DefineQuantity("Code", "code", "string"),
DefineQuantity("Name", "name", "string"),
DefineQuantity("Shortname", "shortname", "string"),
//DefineQuantity("Designator","designator","string"),
DefineQuantity("Descr", "descr", "string"),
DefineQuantity("Comments", "cmnts", "string"),
//DefineQuantity("SPECIFICATION", "spec", "string"),
DefineQuantity("Purpose", "purpose", "string"),
DefineQuantity("Type", "type", "string"),
DefineQuantity("ClassCD", "classcd", "string"),
DefineQuantity("GrpClsCD", "grpclscd", "string"),
DefineQuantity("SystemType", "systype", "string"),
DefineQuantity("Diameter", "diam", "mm"),
DefineQuantity("InnerDiam", "inndiam", "mm"),
DefineQuantity("OuterDiam", "outdiam", "mm"),
DefineQuantity("Radius", "radius", "mm"),
DefineQuantity("Thickness", "thickness", "mm"),
DefineQuantity("SequenceNum", "seqnum", "1"),
DefineQuantity("Number", "number", "1"),
DefineQuantity("CurrIndic", "currind", "string"),
DefineQuantity("User", "user", "nvarchar(50)"),
DefineQuantity("Creator", "creator", "string"),
DefineQuantity("Editor", "editor", "string"),
//DefineQuantity("CreaTime", "creatime", "dt"), // creation time
//DefineQuantity("EdiTime", "editime", "dt"), // editing time
//DefineQuantity("InstTime", "insttime", "dt"),
DefineQuantity("XCoord", "xcoord", "1"),
DefineQuantity("YCoord", "ycoord", "1"),
DefineQuantity("RawGeom", "rawgeom", "bytes"),
DefineQuantity("Geometry", "geometry", "bytes"),
DefineQuantity("Measure", "measure", "m"),
//DefineQuantity("RD", "rd", "string"), // symbolic code lookup
//DefineQuantity("HRD", "hrd", "string"), // hierarchical  code lookup
DefineQuantity("RC", "rc", "1"), // numeric code lookup


// Oracle Connection
let(oraConn, ora::NewConnection(
	"pipe_bashneft/1@olgin:1521/orcl", // oraConnectionString
	5,								   // nOraConnections
	{	"ALTER SESSION SET NLS_TERRITORY = cis"
		, "ALTER SESSION SET CURSOR_SHARING = SIMILAR"
		, "ALTER SESSION SET NLS_NUMERIC_CHARACTERS ='. '"
		, "ALTER SESSION SET NLS_COMP = ANSI"
		, "ALTER SESSION SET NLS_SORT = BINARY"
	}
)..Cached('WExpr:OraConn', 600)),

// MS SQL Connection
let(sqlConn, sql::NewConnection(
	"sa/Pipe1049@probook/PODS7", // sqlConnectionString
	5,						// nSqlConnections
	{ }
)..Cached('WExpr:SqlConn', 600)),

// Declare data loading functions
//db::UseSqlAsFuncsFrom("Pipe.oracle.sql", , oraConn, "Pipe"),
//db::UseSqlAsFuncsFrom("PPM.meta.sql", { 'Raw', 'TimeSlice' }, oraConn, 'PPM'),
db::UseSqlAsFuncsFrom("PPM.meta.sql", { 'TimeSlice' }, oraConn, 'PPM'),
//db::SqlFuncsToText('PPM').._WriteAllText('PPM.unfolded.sql'),
//let( sqls, db::SqlFuncsToDDL('PPM')), sqls[0].._WriteAllText('PPM.genDDL.sql'), sqls[1].._WriteAllText('PPM.drops.sql'),

//solver::DefineProjectionFuncs({'_CLASSCD_PIPE','CLASS_DICT_PIPE'}, { '_NAME_PIPE','_SHORTNAME_PIPE' }, data, pipe::GetClassInfo(data) ),
//
//pods::CodeLookupHelperFuncs(sqlConn, { 'pipeline_type_cl', 'PipelineType' }, 'PPM'),
//// 
//
//let(AT_TIME__XT, DATEVALUE('2019-04-17')),
//let(PIPELINE_ID_PIPE, 5059),
//
////solver:FindSolutionExpr({'PIPELINE_ID_PIPE','AT_TIME__XT'}, {'PU_RAWGEOM_PIPE'})
////solver:FindSolutionExpr({ }, { 'CLASS_DICT_PIPE' })
////solver:FindSolutionExpr({ }, { 'PipeIntCoatKind_CLASSCD_PIPE', 'PipeIntCoatKind_NAME_PIPE', 'PipeNode_NAME_PIPE' })
//solver::FindSolutionExpr({ }, { 'PipelineType_DICT_PPM' })
//	.solver::ExprToExecutable().AtNdx(0)

)