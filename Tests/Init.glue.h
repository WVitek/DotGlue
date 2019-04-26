﻿(
Using('W.Expressions.FuncDefs_DB', 'WExprSql.dll', 'db::'),
Using('W.Expressions.Sql.FuncDefs_Ora', 'WDbOracle.dll', 'ora::'),
Using('W.Expressions.Sql.FuncDefs_MsSql', 'WDbMsSql.dll', 'sql::'),
Using('W.Expressions.FuncDefs_Solver', 'WSolver.dll', 'solver::'),
Using('Pipe.Exercises.FuncDefs_Pipe', 'PipeExcercises.exe', 'pipe::'),
Using('Pipe.Exercises.FuncDefs_PODS7', 'PipeExcercises.exe', 'pods::'),

// Define quantities
DefineQuantity("id", "id", "code"),
DefineQuantity("code", "code", "string"),
DefineQuantity("name", "name", "string"),
DefineQuantity("shortname", "shortname", "string"),
DefineQuantity("designator","designator","string"),
DefineQuantity("description", "descr", "string"),
DefineQuantity("specification", "spec", "string"),
DefineQuantity("purpose", "purpose", "string"),
DefineQuantity("classcd", "classcd", "string"),
DefineQuantity("grpclscd", "grpclscd", "string"),
DefineQuantity("systemtype", "systype", "string"),
DefineQuantity("diameter", "diam", "mm"),
DefineQuantity("thickness", "thickness", "mm"),
DefineQuantity("sequencenum", "seqnum", "1"),
DefineQuantity("currindic", "currind", "string"),
DefineQuantity("user", "user", "string"),
DefineQuantity("creator", "creator", "string"),
DefineQuantity("editor", "editor", "string"),
DefineQuantity("creatime", "creatime", "dt"), // creation time
DefineQuantity("editime", "insttime", "dt"), // editing time
DefineQuantity("insttime", "insttime", "dt"),
DefineQuantity("xcoord", "xcoord", "1"),
DefineQuantity("ycoord", "ycoord", "1"),
DefineQuantity("RawGeom", "RawGeom", "bytes"),
DefineQuantity("cl", "cl", "string"), // code lookup


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
db::UseSqlAsFuncsFrom("Pipe.oracle.sql", , oraConn, "Pipe"),
db::UseSqlAsFuncsFrom("PODS7.ms.sql", , oraConn, "PODS"),

solver::DefineProjectionFuncs({'_CLASSCD_PIPE','CLASS_DICT_PIPE'}, { '_NAME_PIPE','_SHORTNAME_PIPE' }, data, pipe::GetClassInfo(data) ),

pods::CodeLookupHelperFuncs(sqlConn, { 'pipeline_type_cl', 'PipelineType' }, 'PODS7'),
// 

let(AT_TIME__XT, DATEVALUE('2019-04-17')),
let(PIPELINE_ID_PIPE, 5059),

//solver:FindSolutionExpr({'PIPELINE_ID_PIPE','AT_TIME__XT'}, {'PU_RAWGEOM_PIPE'})
//solver:FindSolutionExpr({ }, { 'CLASS_DICT_PIPE' })
//solver:FindSolutionExpr({ }, { 'PipeIntCoatKind_CLASSCD_PIPE', 'PipeIntCoatKind_NAME_PIPE', 'PipeNode_NAME_PIPE' })
solver::FindSolutionExpr({ }, { 'PipelineType_DICT_PODS7' })
	.solver::ExprToExecutable().AtNdx(0)

)