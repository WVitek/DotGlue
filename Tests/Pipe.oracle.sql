--Pipelines
--Трубопроводы OIS Pipe
SELECT 
    "ID трубопровода" AS Pipeline_ID
    ,
    CASE WHEN "Дата изменения">"Дата создания" THEN "Дата изменения" ELSE "Дата создания" END  AS START_TIME,
    "Дата удаления" AS END_TIME,

--************** Administrative bindings via ****************
--target: PODS.EVENT_GROUP_CROSS_REF ************************
	"Предприятие" AS PipeOrg_CLASSCODE,
	"Цех" AS PipeShop_CLASSCODE,
	"Месторождение" AS PipeField_CLASSCODE,
	"Площадка" AS PipeSite_NAME,

--target: PODS.LINE
	"Назначение" AS PipePurpose_CLASSCODE
	,
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
    COALESCE( "Назначение", "Тип трубопроводной сети") AS PipelineType_CLASSCODE
    ,
    "Тип трубопровода" AS PipelineSysType_CLASSCODE
    ,
    CASE WHEN "Дата удаления" IS NULL THEN 'Y' ELSE 'N' END   AS Pipeline_CurrIndic

FROM PIPE_TRUBOPROVOD
;

--Pipeline_To_UT_Link
--Связка трубопровод -> участок трубопровода (UT) OIS Pipe
SELECT
    "ID трубопровода" AS Pipeline_ID
    ,
    CASE WHEN "Дата изменения" > "Дата создания" THEN "Дата изменения" ELSE "Дата создания" END  AS START_TIME
    ,
    "Дата удаления" AS END_TIME
    ,
    "ID участка" AS PU_ID
FROM pipe_uchastok_truboprovod
;

--PipeRoutes
SELECT
--multitarget fields  ***************************************
	-- Pipe.UT: поля записи OIS-Pipe "участок трубопровода", соответствующие нескольким целевым таблицам PODS
	-- Предполагается задавать соответствие с помощью "алиасов"
    "ID участка"  AS UT_ID,
    CASE WHEN "Дата удаления" IS NULL THEN 'Y' ELSE 'N' END   AS UT_CurrIndic,
	CASE WHEN "Дата изменения" > "Дата создания" THEN "Изменено пользователем" ELSE "Создано пользователем" END  AS UT_USER,
	CASE WHEN "Дата ввода" < "Дата реконструкции участка" THEN  "Дата реконструкции участка" ELSE "Дата ввода" END  AS UT_InstallDate,
	"Дата создания"  AS UT_CreateDate,

--target: PODS.ROUTE ****************************************
    CASE WHEN LENGTH("Начало участка")+1+LENGTH("Конец участка") > 254
    THEN CONCAT( SUBSTR( "Начало участка" || '~' || "Конец участка", 1, 253 ), '…' )
    ELSE "Начало участка" || '~' || "Конец участка"
    END  AS PipeRoute_DESCRIPTION
    ,
    COALESCE("Порядок",0) + "Номер нитки"*32  AS PipeRoute_SequenceNum,
    "Назначение" PipeRouteType_CLASSCODE
	,
--target: PODS.PIPE_SEGMENT  ********************************
	"Завод изготовитель"  AS PipeSegManuf_CLASSCODE,
	"Материал трубы"  AS PipeSegMaterial_CLASSCODE,
    LTRIM(RTRIM(UPPER(REPLACE("ГОСТ на трубу"||'|'||"ГОСТ на материал",' ')),'|'),'|')  AS PipeSeg_SPECIFICATION,
    "Класс прочности материала"  AS PipeSegGrade_CLASSCODE,
    GREATEST(D, 9999.9999)  AS PipeSeg_DIAMETER,
    GREATEST(S, 99.9999)  AS PipeSegWall_THICKNESS,

--target: PODS.EXTERNAL_COATING  ****************************
	"Завод изготовитель покр"  AS PipeExtCoatManuf_CLASSCODE,
	"Вид покрытия внешнего"  AS PipeExtCoatKind_CLASSCODE,
	"Конструкция внеш покрытия"  AS PipeExtCoatKind_GRPCLSCODE,
	"Условия нанесений внеш покр"  AS PipeExtCoatWhereAppl_CLASSCODE,
	"ТУ внешнего"  AS PipeExtCoat_SPECIFICATION,

--target: PODS.INTERNAL_COATING  ****************************
	"Завод изг внутреннего покр"  AS PipeIntCoatManuf_CLASSCODE,
	"Вид покрытия внутреннего"  AS PipeIntCoatKind_CLASSCODE,
	"Конструкция внутр покрытия"  AS PipeIntCoatKind_GRPCLSCODE,
	"Условия нанесения внутр покр"  AS PipeIntCoatWhereAppl_CLASSCODE,
	"ТУ внутреннего"  AS PipeIntCoat_SPECIFICATION


FROM pipe_uchastok_truboprovod
;

