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
DefineQuantity("id", "id", ppmIdType),
DefineQuantity("code", "code", "string"),
DefineQuantity("name", "name", "string"),
DefineQuantity("shortname", "shortname", "string"),
DefineQuantity("designator","designator","string"),
DefineQuantity("descr", "descr", "string"),
DefineQuantity("comments", "cmnts", "string"),
DefineQuantity("specification", "spec", "string"),
DefineQuantity("purpose", "purpose", "string"),
DefineQuantity("type", "type", "string"),
DefineQuantity("classcd", "classcd", "string"),
DefineQuantity("grpclscd", "grpclscd", "string"),
DefineQuantity("systemtype", "systype", "string"),
DefineQuantity("diameter", "diam", "mm"),
DefineQuantity("innerdiam", "inndiam", "mm"),
DefineQuantity("outerdiam", "outdiam", "mm"),
DefineQuantity("radius", "radius", "mm"),
DefineQuantity("thickness", "thickness", "mm"),
DefineQuantity("sequencenum", "seqnum", "1"),
DefineQuantity("number", "number", "1"),
DefineQuantity("currindic", "currind", "string"),
DefineQuantity("user", "user", "nvarchar(50)"),
DefineQuantity("creator", "creator", "string"),
DefineQuantity("editor", "editor", "string"),
DefineQuantity("creatime", "creatime", "dt"), // creation time
DefineQuantity("editime", "insttime", "dt"), // editing time
DefineQuantity("insttime", "insttime", "dt"),
DefineQuantity("xcoord", "xcoord", "1"),
DefineQuantity("ycoord", "ycoord", "1"),
DefineQuantity("RawGeom", "RawGeom", "bytes"),
DefineQuantity("geometry", "geometry", "bytes"),
DefineQuantity("measure", "measure", "m"),
DefineQuantity("rd", "rd", "string"), // symbolic code lookup
DefineQuantity("hrd", "hrd", "string"), // hierarchical  code lookup
DefineQuantity("rc", "rc", "1"), // numeric code lookup


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
db::UseSqlAsFuncsFrom("PPM.meta.sql", 'Raw', oraConn, 'PPM'),
//db::SqlFuncsToText('PPM').._WriteAllText('PPM.unfolded.sql'),
let( sqls, db::SqlFuncsToDDL('PPM')), sqls[0].._WriteAllText('PPM.genDDL.sql'), sqls[1].._WriteAllText('PPM.drops.sql'),

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