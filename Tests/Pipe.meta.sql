--AbstractTable='History'
SELECT
--Время начала периода истинности факта
--FixedAlias=1
	CASE WHEN "Дата изменения">"Дата создания" THEN "Дата изменения" ELSE "Дата создания" END  AS START_TIME,
--Время окончания периода истинности факта (NULL равносильно истинности по настоящее время)
--FixedAlias=1
    "Дата удаления" AS END_TIME,

	"Создано пользователем"  AS CreatorUser_ID,
	"Дата создания"  AS Create_Time,
	"Изменено пользователем"  AS EditorUser_ID,
	"Дата изменения"  AS Edit_Time,
	"Удалено пользователем"  AS RemoverUser_ID,
	"Дата удаления"  AS Remove_Time
;

--AsPPM_Pipe
--Substance='Pipe'
SELECT
    ROWNUM as PipeRow_ID,
--Inherits='History'
    Pipe_ID, 
	Level_RD, 
	PipeParent_ID,
--FixedAlias=1
    Pt_ID, 
--FixedAlias=1
	Ut_ID, 
--FixedAlias=1
	Pu_ID,
    "Мероприятие"  AS Action_DESCR,
    "Район"  AS Area_ClCD,
    "Рабочая среда"  AS Fluid_ClCD,
    "Расшифровка бездейств сост"  AS IdleState_ClCD,
    "Тип трубопроводной сети"  AS Network_ClCD,
    "Месторождение"  AS Oilfield_ClCD,
    "Приказ"  AS Order_DESCR,
    "Предприятие"  AS Org_ClCD,
    "Примечание"  AS Pipe_Comments,
    "L"  AS Pipe_Length,
    "Назначение"  AS Purpose_ClCD,
    "Регистрационный номер"  AS Reg_CODE,
    "Область"  AS Region_ClCD,
    "Цех"  AS Shop_ClCD,
    "Площадка"  AS Site_ClCD,
    "Состояние"  AS State_ClCD,
    "Дата изменения состояния"  AS StateChange_TIME,
    "Тип трубопровода"  AS Type_ClCD
FROM (
SELECT
	Pt."ID трубопровода"  AS Pipe_ID,
	Pt."ID трубопровода"  AS Pt_ID,
	NULL  AS Ut_ID,
	NULL  AS Pu_ID,
	NULL  AS PipeParent_ID,
	'Pipeline'  AS Level_RD,
----
    NULL  AS "Мероприятие",
    Pt."Район",
    Pt."Дата создания",
    Pt."Создано пользователем",
    Pt."Дата изменения",
    Pt."Изменено пользователем",
    Pt."Рабочая среда",
    NULL  AS "Расшифровка бездейств сост",
    Pt."Тип трубопроводной сети",
    Pt."Месторождение",
    NULL  AS "Приказ",
    Pt."Предприятие",
    Pt."Примечание",
    NULL  AS "L",
    Pt."Назначение",
    Pt."Регистрационный номер",
    Pt."Область",
    Pt."Дата удаления",
    Pt."Удалено пользователем",
    Pt."Цех",
    Pt."Площадка",
    NULL  AS "Состояние",
    NULL  AS "Дата изменения состояния",
    Pt."Тип трубопровода"
FROM pipe_truboprovod Pt
UNION ALL
SELECT
	Ut."ID участка"  AS Pipe_ID,
	NULL  AS Pt_ID,
	Ut."ID участка"  AS Ut_ID,
	NULL  AS Pu_ID,
	Ut."ID трубопровода"  AS PipeParent_ID,
	'PipelineSection'  AS Level_RD,
----
    Ut."Мероприятие",
    Ut."Район",
    Ut."Дата создания",
    Ut."Создано пользователем",
    Ut."Дата изменения",
    Ut."Изменено пользователем",
    Ut."Рабочая среда",
    Ut."Расшифровка бездейств сост",
    Ut."Тип трубопроводной сети",
    Ut."Месторождение",
    Ut."Приказ",
    Ut."Предприятие",
    Ut."Примечание",
    Ut."L",
    Ut."Назначение",
    Ut."Регистрационный номер",
    Ut."Область",
    Ut."Дата удаления",
    Ut."Удалено пользователем",
    Ut."Цех",
    Ut."Площадка",
    Ut."Состояние",
    Ut."Дата изменения состояния",
    Ut."Тип трубопровода"
FROM pipe_uchastok_truboprovod Ut
UNION ALL
SELECT
	Pu."ID простого участка"  AS Pipe_ID,
	NULL  AS Pt_ID,
	NULL AS Ut_ID,
	Pu."ID простого участка"  AS Pu_ID,
	Pu."ID участка"  AS PipeParent_ID,
	'PipelineSimpleSection'  AS Level_RD,
----
    Pu."Мероприятие",
    NULL  AS "Район",
    Pu."Дата создания",
    Pu."Создано пользователем",
    Pu."Дата изменения",
    Pu."Изменено пользователем",
    Ut."Рабочая среда",
    Pu."Расшифровка бездейств сост",
    Ut."Тип трубопроводной сети",
    Ut."Месторождение",
    Pu."Приказ",
    NULL  AS "Предприятие",
    Pu."Примечание",
    Pu."L",
    Ut."Назначение",
    NULL  AS "Регистрационный номер",
    NULL  AS "Область",
    Pu."Дата удаления",
    Pu."Удалено пользователем",
    Ut."Цех",
    NULL  AS "Площадка",
    Pu."Состояние",
    Pu."Дата изменения состояния",
    NULL  AS "Тип трубопровода"
FROM pipe_prostoy_uchastok Pu
JOIN pipe_uchastok_truboprovod Ut ON Ut."ID участка"=Pu."ID участка"
)
;

--Pipe_Truboprovod
--Непустые столбцы таблицы pipe_truboprovod из OIS Pipe
--Substance='Pt'
SELECT
    "ID трубопровода" AS Pt_ID,
--Inherits='History'

--FixedAlias=1
	"ID схемы"  AS Shema_ID,
	"Начало трубопровода"  AS BegNode_DESCR,
	"Конец трубопровода"  AS EndNode_DESCR,
	"Узел начала трубопровода"  AS BegNode_ID,
	"Узел конца трубопровода"  AS EndNode_ID,

	"Регистрационный номер"  AS Reg_CODE,
	"Предприятие"  AS Org_ClCD,
	"Месторождение"  AS Oilfield_ClCD,
	"Цех"  AS Shop_ClCD,
	"Площадка"  AS Site_ClCD,
	"Тип трубопроводной сети"  AS Network_ClCD,
	"Тип трубопровода"  AS Type_ClCD,
	"Назначение"  AS Purpose_ClCD,
	"Рабочая среда"  AS Fluid_ClCD,
	"Область"  AS Region_ClCD,
	"Район"  AS Area_ClCD,
	"Длина"  AS Length,
	"Давление в начале трубопровода"  AS Beg_PRESSURE__ATM,
	"Давление в конце трубопровода"  AS End_PRESSURE__ATM,
	"Примечание"  AS Comments,
--FixedAlias=1
	"ID расчетной схемы"  AS CalcShema_ID,
	"Проектное давление"  AS Design_PRESSURE__ATM
FROM PIPE_TRUBOPROVOD
;

--Pipe_PT2UT_A
--Связка трубопровод (Pt) -> участок трубопровода (Ut) из OIS Pipe
SELECT
    "ID трубопровода" AS Pt_ID,
	null INS_OUTS_SEPARATOR,
    "ID участка" AS Ut_ID
FROM pipe_uchastok_truboprovod
----WHERE L>100
;

------Pipe_PT2UT_B
------Связка трубопровод (Pt) -> участок трубопровода (Ut) из OIS Pipe
----SELECT
----    "ID трубопровода" AS Pt_ID,
----	null INS_OUTS_SEPARATOR,
----    "ID участка" AS Ut_ID
----FROM pipe_uchastok_truboprovod
----WHERE NOT (L>100)
----;

--Pipe_Uchastok_Truboprovod
--Непустые столбцы таблицы pipe_uchastok_truboprovod из OIS Pipe
--Substance='Ut'
SELECT
    "ID участка" AS Ut_ID,
--Inherits='History'
    "ID трубопровода" AS Pt_ID,
	"Номер нитки"  AS Branch_NUMBER,
	"Порядок"  AS SequenceNum,
	"Начало участка"  AS BegNode_DESCR,
	"Конец участка"  AS EndNode_DESCR,
	"Предприятие"  AS Org_ClCD,
	"Месторождение"  AS Oilfield_ClCD,
	"Цех"  AS Shop_ClCD,
	"Площадка"  AS Site_ClCD,
	"Тип трубопроводной сети"  AS Network_ClCD,
	"Тип трубопровода"  AS Type_ClCD,
	"Назначение"  AS Purpose_ClCD,
	"Рабочая среда"  AS Fluid_ClCD,
	"Область"  AS Region_ClCD,
	"Район"  AS Area_ClCD,
	"Регистрационный номер"  AS Reg_CODE,
	"L"  AS Length,
	"D"  AS OuterDiam,
	"S"  AS Thickness,
	"Состояние"  AS State_ClCD,
	"Дата изменения состояния"  AS StateChange_TIME,
	"Расшифровка бездейств сост"  AS IdleState_ClCD,
	"Отбраковка"  AS Rejection_CODE,
	"P рабочее"  AS Work_PRESSURE__ATM,
	"P в начале участка"  AS Beg_PRESSURE__ATM,
	"P в конце участка"  AS End_PRESSURE__ATM,
	"Расход агента"  AS ReagentRate_NUMBER,
	"Температура"  AS Temperature,
	"Категория"  AS Category_ClCD,
	"Дата ввода"  AS Comissn_TIME,

	"Балансовая стоимость"  AS BookValue_Money__RUB,
	"Норма амортизации"  AS DepnRate_NUMBER,
	"Инвентарный номер"  AS InventoryNumber_DESCR,
	"Первоначальная стоимость"  AS InitialCost_Money__RUB,
	"Дата инвентаризации"  AS Inventory_TIME,
	"Строительная организация"  AS BuildOrg_ClCD,
	"Проектная организация"  AS DesignOrg_ClCD,
	"Проект"  AS Design_DESCR,
	"Рабочие чертежи"  AS DesignDraw_DESCR,
	"Тип прокладки"  AS LayingType_ClCD,
	"Конструкция опор"  AS SupportType_ClCD,
	"Высота трубопровода от земли"  AS Elevation_Height,
	"Расстояние между опорами"  AS SupportInterval_Length,
	"Глубина прокладки"  AS DEPTH,
	"Тип соединения"  AS JoinType_ClCD,
	"Тип грунта"  AS GroundType_ClCD,
	"Тип сварки"  AS WeldType_ClCD,
	"Контроль сварных швов"  AS WeldInspection_PERCENT,
	"Присадочные материалы"  AS WeldFiller_DESCR,
	"Примечание по сварке"  AS Weld_COMMENTS,
	"Тип трубы"  AS PipeType_ClCD,
	"ГОСТ на трубу"  AS Spec_DESCR,
	"Материал трубы"  AS Material_ClCD,
	"ГОСТ на материал"  AS MaterialSpec_DESCR,
	"Эквивалентная шероховатость"  AS EquivRoughness_Height,
	"Завод изготовитель"  AS Manuf_ClCD,
	"Вид защиты сварного шва"  AS WeldJointProt_ClCD,
	"Вид покрытия внешнего"  AS OuterCoat_ClCD,
	"ТУ внешнего"  AS OuterCoatSpec_DESCR,
	"Вид покрытия внутреннего"  AS InnerCoat_ClCD,
	"ТУ внутреннего"  AS InnerCoatSpec_DESCR,
	"Завод изготовитель покр"  AS OuterCoatManuf_ClCD,
	"Скорость коррозии ГОСТ"  AS CorrRateGOST_NUMBER,
	"S отбраковочная"  AS Rejection_Thickness,
	"Примечание"  AS Comments,

	"Вид работ по консервации"  AS ConservWork_DESCR,
	"Год строительства"  AS BuildYear_TIME__YR,
	"Вид консерванта"  AS ConservType_ClCD,
	"Проверен"  AS IsChecked_ClCD,
	"Резерв 1"  AS Extra1_ClCD,
	"Резерв 2"  AS Extra2_ClCD,
	"Наличие исполнит док"  AS WithExecDocs_ClCD,
	"Завод изг внутреннего покр"  AS InnerCoatManuf_ClCD,
	"Тип теплоизоляции"  AS ThermInsulType_ClCD,
	"Номер по бухучету"  AS AccountingNumber_DESCR,
	"Завод изготовитель защиты шва"  AS WeldJointProtManuf_ClCD,
	"ГОСТ на защиту сварного шва"  AS WeldJoinProtSpec_DESCR,
	"Представитель мон организации"  AS BuilderRepres_DESCR,
	"Номер исполнительной док"  AS ExecDocs_DESCR,
	"Температура2"  AS Second_Temperature,
	"Литера категории"  AS CategoryLetter_ClCD,
	"Допустимое давление"  AS Allowable_Pressure__ATM,
	"Регистрирующее лицо"  AS RegistrantRepres_DESCR,
	"Категория района"  AS AreaCategory_NUMBER,
	"Длина по бух.учету"  AS Accounting_LENGTH,
	"Место хранения исп.документ"  AS ExecDocsStorage_DESCR,
	"ID узла площадки"  AS SiteNode_ID,
	"ГОСТ на присадочный материал"  AS WeldFillerSpec_DESCR,
	"Дата реконструкции участка"  AS Renovation_TIME,
	"Наружная защита шва"  AS JointOuterProt_ClCD,
	"Рег номер ОПО"  AS HazardObjRegNum_DESCR,
	"Приказ"  AS Order_DESCR,
	"Мероприятие"  AS Action_DESCR,
	"Длина по верификации"  AS Verified_Length,
	"Собственник основного средства"  AS AssetOwner_ClCD,
	"Условия нанесения внутр покр"  AS InnerCoatApplSite_ClCD,
	"Конструкция внутр покрытия"  AS InnerCoatType_ClCD,
	"Условия нанесений внеш покр"  AS OuterCoatApplSite_ClCD,
	"Конструкция внеш покрытия"  AS OuterCoatType_ClCD,
	"Срок службы предшествующего"  AS PrevLife_TimeSpan__YR,
	"Тип внутр защиты соединени"  AS JointInnerProt_ClCD,
	"Категория земель"  AS LandCategory_ClCD,
	"Наличие блуждающих токов"  AS WithStrayCurrents_ClCD,
	"Коррозионная активность грунта"  AS SoilCorrosivity_ClCD,
	"Наличие ПСД"  AS DesignDocsState_ClCD,
	"Номер ПСД"  AS DesignDocsNumber_DESCR,
	"Состояние ПСД"  AS DesignDocsStage_ClCD,
	"Плановый срок получения ПСД"  AS DesignDocsPlanned_TIME,
	"Ожидаемый срок получения ПСД"  AS DesignDocsExpected_TIME,
	"Фактический срок получения ПСД"  AS DesignDocsFact_TIME,
	"Класс прочности материала"  AS MaterialStrGrade_ClCD,
	"Нормативный срок экспл"  AS StdLife_TimeSpan__YR,
	"Программа строительства"  AS BuildProgram_ClCD
FROM pipe_uchastok_truboprovod
;

--Pipe_UT2PU
--Связка участок трубопровода (Ut) -> простой участок (Pu) из OIS Pipe
SELECT
    "ID участка" AS Ut_ID,
	null INS_OUTS_SEPARATOR,
    "ID простого участка" AS Pu_ID
FROM pipe_prostoy_uchastok
;

--Pipe_Prostoy_Uchastok
--Непустые столбцы таблицы pipe_prostoy_uchastok из OIS Pipe
--Substance='Pu'
SELECT
    "ID простого участка" AS Pu_ID,
--Inherits='History'
    "ID участка" AS Ut_ID,
	"Начало простого участка"  AS BegNode_DESCR,
	"Конец простого участка"  AS EndNode_DESCR,
	"Узел начала участка"  AS BegNode_ID,
	"Узел конца участка"  AS EndNode_ID,
	"L"  AS Length,
	"Примечание"  AS Comments,
	"Координаты"  AS RawGeom,
	"Состояние"  AS State_ClCD,
	"Газовая линия"  AS WithGas_ClCD,
	"Координаты2"  AS Second_RawGeom,
	"Координаты3"  AS Third_RawGeom,
	"Расшифровка бездейств сост"  AS IdleState_ClCD,
	"Дата изменения состояния"  AS StateChange_TIME,
	"Вид работ по вытеснению нефт"  AS OilDisplType_ClCD,
	"Обводненость бездействия"  AS Idle_Watercut,
	"Плотность бездействия"  AS Idle_Density,
----	"Нахождение в ЗОВ"  AS ???,
	"Соли бездействия"  AS IdleSalinity_Factor__PPM,
	"Мех примеси бездействия"  AS IdleMechImp_Factor,
	"Приказ"  AS Order_DESCR,
	"Мероприятие"  AS Action_DESCR,
	"Перемычка"  AS IsJumper_ClCD,
	"Коэффициент заполнения"  AS Fill_Factor
FROM pipe_prostoy_uchastok
;

--Pipe_PU_Coords
SELECT
    pu."ID простого участка"  AS Pu_ID,
	null INS_OUTS_SEPARATOR,
    p0.X  AS BegNode_XCoord, 
    p0.Y  AS BegNode_YCoord,
    p0."Альтитуда узла"  AS BegNode_ZCoord,
    p1.X  AS EndNode_XCoord, 
    p1.Y  AS EndNode_YCoord,
    p1."Альтитуда узла"  AS EndNode_ZCoord
FROM pipe_prostoy_uchastok pu
JOIN pipe_node p0 ON pu."Узел начала участка" = p0."ID узла"
JOIN pipe_node p1 ON pu."Узел конца участка" = p1."ID узла"
;

--Pipe_PU2PN
--Связка простой участок (Pu) -> узел (PipeNode) из OIS Pipe
SELECT 
	"ID простого участка"  AS Pu_ID,
	null INS_OUTS_SEPARATOR,
	"ID узла"  AS PipeNode_ID
FROM pipe_prostoy_uchastok UNPIVOT ("ID узла" for Node IN ("Узел начала участка" as 0, "Узел конца участка" as 1) )
;

--Pipe_Node
--Непустые столбцы таблицы pipe_node из OIS Pipe
--Substance='PipeNode'
SELECT 
	"ID узла"  AS ID,
--Inherits='History'

--FixedAlias=1
	"ID тип узла"  AS NodeType_ID,
--FixedAlias=1
	"ID шаблон узла"  AS NodeTmpl_ID,
	"ID родителя"  AS Parent_ID,
	"Код объекта"  AS Obj_ID,
	"Название"  AS Descr,
	"Альтитуда узла"  AS ZCoord,
	"Предприятие"  AS Org_ClCD,
	"Месторождение"  AS Oilfield_ClCD,
	"Цех"  AS Shop_ClCD,
	"X"  AS XCoord,
	"Y"  AS YCoord,
	"X2"  AS Second_XCoord,
	"Y2"  AS Second_YCoord,
	"X3"  AS Third_XCoord,
	"Y3"  AS Third_YCoord
FROM pipe_node
;

--ClassDictData[]
--Данные справочника CLASS для lookup-функции
select 
	0 GrpClsDictData_ID_TMP, -- fictive key for grouping values
    cd_1  AS ClassItem_ClCD,
    ne_1  AS ClassItem_NAME,
    ns_1  AS ClassItem_SHORTNAME
from class
order by cd_1
;