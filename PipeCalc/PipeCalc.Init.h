(
Using('W.Expressions.FuncDefs_DB', 'W.Expr.Sql.dll', 'db::'),
Using('W.Expressions.Sql.FuncDefs_Ora', 'W.DB.Oracle.dll', 'ora::'),

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

)