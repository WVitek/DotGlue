(
Using('W.Expressions.FuncDefs_DB', 'WExprSql.dll', 'db:'),
Using('W.Expressions.Sql.FuncDefs_Ora', 'WDbOracle.dll', 'ora:'),

let(oraConnectionString, "sa/Pipe1049@olgin:1521/orcl"),
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

DefineQuantity("designator","designator","string"),
DefineQuantity("description", "descr", "string"),
DefineQuantity("type", "type", "string"),
DefineQuantity("systemtype", "systemtype", "string"),
DefineQuantity("currindicator", "currind", "string"),

db:UseSqlAsFuncsFrom("Pipe.oracle.sql",,oraConn)

)