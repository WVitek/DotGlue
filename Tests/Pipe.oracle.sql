--PipeLines
--Трубопроводы OIS Pipe
SELECT 
    "ID трубопровода" LINE_ID
    ,
    CASE WHEN "Дата изменения">"Дата создания" THEN "Дата изменения" ELSE "Дата создания" END  START_TIME
    ,
    "Дата удаления" END_TIME
    ,
    CASE WHEN LENGTH( "Начало трубопровода" || '~' || "Конец трубопровода" ) > 50 
    THEN CONCAT( SUBSTR( "Начало трубопровода" || '~' || "Конец трубопровода", 1, 49 ), '…' )
    ELSE "Начало трубопровода" || '~' || "Конец трубопровода"
    END LINE_DESIGNATOR
    ,
    CASE WHEN LENGTH( "Начало трубопровода" || '~' || "Конец трубопровода" ) > 254 
    THEN CONCAT( SUBSTR( "Начало трубопровода" || '~' || "Конец трубопровода", 1, 253 ), '…' )
    ELSE "Начало трубопровода" || '~' || "Конец трубопровода"
    END LINE_DESCRIPTION
    ,
    COALESCE( "Назначение", "Тип трубопроводной сети") LINE_TYPE_CL
    ,
    "Тип трубопровода" Line_SystemType_CL
    ,
    CASE WHEN "Дата удаления" IS NULL THEN 'Y' ELSE 'N' END   Line_CurrIndicator_LF
FROM PIPE_TRUBOPROVOD
;
