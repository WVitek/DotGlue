(
Using('W.Expressions.FuncDefs_DB', 'W.Expr.Sql.dll', 'db::'),
Using('W.Expressions.Sql.FuncDefs_Ora', 'W.DB.Oracle.dll', 'ora::'),
Using('W.Expressions.Sql.FuncDefs_MsSql', 'W.DB.MsSql.dll', 'sql::'),

let(ppmIdType, 'uniqueidentifier'),
let(ppmStr, 'nvarchar'),
let(ppmTime, 'datetime2(3)'),

_include('Quantities.h'),

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