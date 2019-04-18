(
Using('W.Expressions.FuncDefs_DB', 'WExprSql.dll', 'db:'),
Using('W.Expressions.Sql.FuncDefs_Ora', 'WDbOracle.dll', 'ora:'),
Using('W.Expressions.FuncDefs_Solver', 'WSolver.dll', 'solver:'),
Using('Pipe.Exercises.FuncDefs_Pipe', 'PipeExcercises.exe', 'pipe:'),

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
DefineQuantity("createdate", "createdate", "time"),
DefineQuantity("installdate", "installdate", "time"),
DefineQuantity("xcoord", "xcoord", "1"),
DefineQuantity("ycoord", "ycoord", "1"),
DefineQuantity("raw", "raw", "bytes"),

// Oracle Connection
let(oraConnectionString, "pipe_bashneft/1@olgin:1521/orcl"),
let(nOraConnections, 5),
let(nlsTerritory, 'ALTER SESSION SET NLS_TERRITORY = cis'),

let(oraConn, ora:NewConnection(
	oraConnectionString,
	nOraConnections,
	{ nlsTerritory
		, "ALTER SESSION SET CURSOR_SHARING = SIMILAR"
		, "ALTER SESSION SET NLS_NUMERIC_CHARACTERS ='. '"
		, "ALTER SESSION SET NLS_COMP = ANSI"
		, "ALTER SESSION SET NLS_SORT = BINARY"
	}
)..Cached('WExpr:OraConn', 600)
),

// Declare data loading functions
db:UseSqlAsFuncsFrom("Pipe.oracle.sql",,oraConn,"Pipe"),

solver:DefineProjectionFuncs({'_CLASSCD_PIPE','CLASS_DICT_PIPE'}, { '_NAME_PIPE','_SHORTNAME_PIPE' }, data, pipe:GetClassInfo(data) ),
// 

let(AT_TIME__XT, DATEVALUE('2019-04-17')),
let(PIPELINE_ID_PIPE, 5059),

//solver:FindSolutionExpr({'PIPELINE_ID_PIPE','AT_TIME__XT'}, {'PUGEOMETRY_RAW_PIPE'})
//solver:FindSolutionExpr({ }, { 'CLASS_DICT_PIPE' })
solver:FindSolutionExpr({ }, { 'PipeIntCoatKind_CLASSCD_PIPE', 'PipeIntCoatKind_NAME_PIPE', 'PipeNode_NAME_PIPE' })
//solver:FindSolutionExpr({ }, { 'PipeSite_NAME_PIPE' })
	.solver:ExprToExecutable().AtNdx(0)

)