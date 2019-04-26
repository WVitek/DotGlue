--Pipelines
--Трубопроводы OIS Pipe
SELECT 
    "ID трубопровода" AS Pipeline_ID
    ,
    CASE WHEN "Дата изменения">"Дата создания" THEN "Дата изменения" ELSE "Дата создания" END  AS START_TIME,
    "Дата удаления" AS END_TIME,

--************** Administrative bindings via ****************
--target: PODS.EVENT_GROUP_CROSS_REF ************************
	"Предприятие" AS PipeOrg_ClassCD,
	"Цех" AS PipeShop_ClassCD,
	"Месторождение" AS PipeField_ClassCD,
	"Площадка" AS PipeSite_NAME,

--target: PODS.LINE
    CASE WHEN LENGTH( "Начало трубопровода" || '~' || "Конец трубопровода" ) > 50 
    THEN CONCAT( SUBSTR( "Начало трубопровода" || '~' || "Конец трубопровода", 1, 49 ), '…' )
    ELSE "Начало трубопровода" || '~' || "Конец трубопровода"
    END AS Pipeline_DESIGNATOR
    ,
    CASE WHEN LENGTH( "Начало трубопровода" || '~' || "Конец трубопровода" ) > 254
    THEN CONCAT( SUBSTR( "Начало трубопровода" || '~' || "Конец трубопровода", 1, 253 ), '…' )
    ELSE "Начало трубопровода" || '~' || "Конец трубопровода"
    END AS Pipeline_DESCRIPTION
    ,
    "Назначение"  AS PipeType_ClassCD
	,
	"Тип трубопроводной сети"  AS PipeLocation_ClassCD
    ,
    "Тип трубопровода" AS PipelineSysType_ClassCD
    ,
    CASE WHEN "Дата удаления" IS NULL THEN 'Y' ELSE 'N' END   AS Pipeline_CurrIndic

FROM PIPE_TRUBOPROVOD
;

--Pipeline_To_UT_Link
--Связка трубопровод -> участок трубопровода (UT) OIS Pipe
SELECT
    "ID трубопровода" AS Pipeline_ID,
    CASE WHEN "Дата изменения" > "Дата создания" THEN "Дата изменения" ELSE "Дата создания" END  AS START_TIME,
    "Дата удаления" AS END_TIME,
    "ID участка" AS UT_ID
FROM pipe_uchastok_truboprovod
;

--PipeRoutes
SELECT
    "ID участка"  AS UT_ID,
	null INS_OUTS_SEPARATOR,

--multitarget fields  ***************************************
	-- Pipe.UT: поля записи OIS-Pipe "участок трубопровода", соответствующие нескольким целевым таблицам PODS
	-- Предполагается задавать соответствие с помощью "алиасов"
    CASE WHEN "Дата удаления" IS NULL THEN 'Y' ELSE 'N' END   AS UT_CurrIndic,
	CASE WHEN "Дата изменения" > "Дата создания" THEN "Изменено пользователем" ELSE "Создано пользователем" END  AS UT_USER,
	CASE WHEN "Дата ввода" < "Дата реконструкции участка" THEN  "Дата реконструкции участка" ELSE "Дата ввода" END  AS UT_InstTime,
	"Дата создания"  AS UT_CreaTime,

--target: PODS.ROUTE ****************************************
    CASE WHEN LENGTH("Начало участка")+1+LENGTH("Конец участка") > 254
    THEN CONCAT( SUBSTR( "Начало участка" || '~' || "Конец участка", 1, 253 ), '…' )
    ELSE "Начало участка" || '~' || "Конец участка"
    END  AS PipeRoute_DESCRIPTION
    ,
    COALESCE("Порядок",0) + "Номер нитки"*32  AS PipeRoute_SequenceNum,
    "Назначение" PipeRouteType_ClassCD
	,
--target: PODS.PIPE_SEGMENT  ********************************
	"Завод изготовитель"  AS PipeSegManuf_ClassCD,
	"Материал трубы"  AS PipeSegMaterial_ClassCD,
    LTRIM(RTRIM(UPPER(REPLACE("ГОСТ на трубу"||'|'||"ГОСТ на материал",' ')),'|'),'|')  AS PipeSeg_SPECIFICATION,
    "Класс прочности материала"  AS PipeSegGrade_ClassCD,
    GREATEST(D, 9999.9999)  AS PipeSeg_DIAMETER,
    GREATEST(S, 99.9999)  AS PipeSegWall_THICKNESS,

--target: PODS.EXTERNAL_COATING  ****************************
	"Завод изготовитель покр"  AS PipeExtCoatManuf_ClassCD,
	"Вид покрытия внешнего"  AS PipeExtCoatKind_ClassCD,
	"Конструкция внеш покрытия"  AS PipeExtCoatKind_GrpClsCD,
	"Условия нанесений внеш покр"  AS PipeExtCoatWhrApl_ClassCD,
	"ТУ внешнего"  AS PipeExtCoat_SPECIFICATION,

--target: PODS.INTERNAL_COATING  ****************************
	"Завод изг внутреннего покр"  AS PipeIntCoatManuf_ClassCD,
	"Вид покрытия внутреннего"  AS PipeIntCoatKind_ClassCD,
	"Конструкция внутр покрытия"  AS PipeIntCoatKind_GrpClsCD,
	"Условия нанесения внутр покр"  AS PipeIntCoatWhrApl_ClassCD,
	"ТУ внутреннего"  AS PipeIntCoat_SPECIFICATION

FROM pipe_uchastok_truboprovod
;

--UT_To_PU_Link
--Связка участок трубопровода (UT) -> простой участок (PU) OIS Pipe
SELECT
    "ID участка" AS UT_ID,
    CASE WHEN "Дата изменения" > "Дата создания" THEN "Дата изменения" ELSE "Дата создания" END  AS START_TIME,
    "Дата удаления" AS END_TIME,
    "ID простого участка" AS PU_ID
FROM pipe_prostoy_uchastok
;

--PipeSeries
SELECT
    "ID простого участка"  AS PU_ID,
	null INS_OUTS_SEPARATOR,
    CASE WHEN pu."Дата удаления" IS NULL THEN 'Y' ELSE 'N' END   AS PU_CurrIndic,
	CASE WHEN pu."Дата изменения" > pu."Дата создания" THEN pu."Изменено пользователем" ELSE pu."Создано пользователем" END  AS PU_USER,
	L  AS PU_LENGTH,

--target: PODS.SERIES
    CASE WHEN LENGTH("Начало простого участка")+1+LENGTH("Конец простого участка") > 254
    THEN CONCAT( SUBSTR( "Начало простого участка" || '~' || pu."Конец простого участка", 1, 253 ), '…' )
    ELSE "Начало простого участка" || '~' || "Конец простого участка"
    END  AS PipeSeries_DESCRIPTION

FROM pipe_prostoy_uchastok pu
;

--PU_Coords
SELECT
    pu."ID простого участка"  AS PU_ID,
	null INS_OUTS_SEPARATOR,

--target: PODS.ATTACHED_GEOMETRY
	pu."Координаты"  AS PU_RawGeom,
    p0.X  AS PUbeg_XCoord, 
    p0.Y  AS PUbeg_YCoord,
    p1.X  AS PUend_XCoord, 
    p1.Y  AS PUend_YCoord

FROM pipe_prostoy_uchastok pu
JOIN pipe_node p0 ON pu."Узел начала участка" = p0."ID узла"
JOIN pipe_node p1 ON pu."Узел конца участка" = p1."ID узла"
;

--PU_Nodes
SELECT 
	"ID простого участка"  AS PU_ID,
	null INS_OUTS_SEPARATOR,
	"ID узла"  AS PipeNode_ID
FROM pipe_prostoy_uchastok UNPIVOT ("ID узла" for Node IN ("Узел начала участка" as 0, "Узел конца участка" as 1) )
;

--PipeNodes
SELECT 
	"ID узла"  AS PipeNode_ID,
    CASE WHEN "Дата изменения" > "Дата создания" THEN "Дата изменения" ELSE "Дата создания" END  AS START_TIME,
    "Дата удаления" AS END_TIME,

	CASE WHEN "Дата изменения" > "Дата создания" THEN "Изменено пользователем" ELSE "Создано пользователем" END  AS PipeNode_USER,
	X  AS PipeNode_XCoord,
	Y  AS PipeNode_YCoord,
	"Название"  AS PipeNode_NAME

FROM pipe_node
;

--ClassDictData[]
--Данные справочника CLASS для lookup-функции
select 
	0 GrpCLDIDA_ID_TMP, -- key for grouping values
    cd_1  AS ClassItem_ClassCD,
    ne_1  AS ClassItem_NAME,
    ns_1  AS ClassItem_SHORTNAME
from class
order by cd_1
;