--Pipelines
--Трубопроводы OIS Pipe
SELECT 
    "ID трубопровода" AS Pipeline_ID
    ,
    CASE WHEN "Дата изменения">"Дата создания" THEN "Дата изменения" ELSE "Дата создания" END  AS START_TIME
    ,
    "Дата удаления" AS END_TIME
    ,
	"Предприятие" AS PipeDept_CODE
	,
	"Цех" AS PipeShop_CODE
	,
	"Месторождение" AS PipeField_CODE
	,
	"Площадка" AS PipeSite_Name
	,
	"Назначение" AS PipePurpose_CODE
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
    COALESCE( "Назначение", "Тип трубопроводной сети") AS PipelineType_CODE
    ,
    "Тип трубопровода" AS PipelineSysType_CODE
    ,
    CASE WHEN "Дата удаления" IS NULL THEN 'Y' ELSE 'N' END   AS Pipeline_CurrIndic
FROM PIPE_TRUBOPROVOD
;

--Pipeline_To_PipeRoute_Link
SELECT
    "ID трубопровода" AS Pipeline_ID
    ,
    CASE WHEN "Дата изменения" > "Дата создания" THEN "Дата изменения" ELSE "Дата создания" END  AS START_TIME
    ,
    "Дата удаления" AS END_TIME
    ,
    "ID участка" AS PipeRoute_ID
FROM pipe_uchastok_truboprovod
;

--PipeRoutes
SELECT
    "ID участка" AS PipeRoute_ID
    ,
    CASE WHEN LENGTH( "Начало участка" || '~' || "Конец участка" ) > 254 
    THEN CONCAT( SUBSTR( "Начало участка" || '~' || "Конец участка", 1, 253 ), '…' )
    ELSE "Начало участка" || '~' || "Конец участка"
    END  AS PipeRoute_DESCRIPTION
    ,
    COALESCE("Порядок",0) + "Номер нитки"*32  AS PipeRoute_SequenceNum
    ,
    "Назначение" PipeRouteType_CODE
	,
    CASE WHEN "Дата удаления" IS NULL THEN 'Y' ELSE 'N' END   AS PipeRoute_CurrIndic
	,
	CASE WHEN "Дата изменения" > "Дата создания" THEN "Изменено пользователем" ELSE "Создано пользователем" END  AS PipeRoute_USER
	,
	"Дата создания"  AS PipeRoute_CreateDate,
	"Завод изготовитель"  AS PipeSegManuf_CODE,
	"Материал трубы"  AS PipeSegMaterial_CODE
	,
    LTRIM(RTRIM(UPPER(REPLACE("ГОСТ на трубу"||'|'||"ГОСТ на материал",' ')),'|'),'|')  AS PipeSeg_SPECIFICATION
	,
    "Класс прочности материала"  AS PipeSegGrade_CODE,
    GREATEST(D, 9999.9999)  AS PipeSeg_DIAMETER,
    GREATEST(S, 99.9999)  AS PipeSegWall_THICKNESS
	,
	CASE WHEN "Дата ввода" < "Дата реконструкции участка" THEN  "Дата реконструкции участка" ELSE "Дата ввода" END  AS PipeSeg_InstallDate
	,
	CASE WHEN "Дата изменения" > "Дата создания" THEN "Изменено пользователем" ELSE "Создано пользователем" END  AS PipeSeg_USER

FROM pipe_uchastok_truboprovod
;
