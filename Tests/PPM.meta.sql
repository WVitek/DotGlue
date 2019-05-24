--AbstractTable='TableBase'
SELECT
--Уникальный ID записи
--PK=1  Type=ppmIdType
	Row_ID,

--ID сущности, свойства которой описывает запись
--Type=ppmIdType
	ID
;

--AbstractTable='Named'
SELECT
--Обязательное имя сущности
--Type=ppmStr&'(255)' NotNull=1
	Name
;

--AbstractTable='Geometry'
SELECT
--Поле для хранения/кеширования геометрической характеристики. Формат зависит от СУБД
--Type='geometry'
	Geometry
;

--AbstractTable='History'
SELECT
--Время начала периода истинности факта
--NotNull=1  FixedAlias=1  Type='date'
	from_date  AS START_TIME,
--Время окончания периода истинности факта (NULL равносильно истинности по настоящее время)
--FixedAlias=1  Type='date'
	to_date  AS END_TIME
;

--AbstractTable='Describe'
SELECT
--Дополнительная информация о свойстве (сущности), хранящемся в этой записи БД
--Type=ppmStr&'(255)'
	Description,
--Дополнительный комментарий в свободной форме, заполняемый оператором, поставщиком или другим участником бизнеса
--Type=ppmStr&'(255)'
	Comments
;

--AbstractTable='Audit'
SELECT
--Время редактирования записи
--Type='date'
	Edit_Time,
--Пользователь, отредактировавший запись
--Type=ppmStr&'(50)'
	Editor_User,
--Время создания записи
--NotNull=1  Type='date'
	Create_Time,
--Пользователь, создавший запись
--NotNull=1  Type=ppmStr&'(50)'
	Creator_User
;

--AbstractTable='LrsPoint'
--Точка в LRS-координатах
SELECT
--Начальный сегмент (маршрута Route)
--FixedAlias=1  Type=ppmIdType
	Route_ID,
--Положение на начальном сегменте
--FixedAlias=1  Type='number(15,3)'
	Route_Measure
;

--AbstractTable='TwoLrsPoints'
--Две точки в LRS-координатах
SELECT
--Начальный сегмент (маршрута Route)
--NotNull=1  FixedAlias=1  Type=ppmIdType
	FromRoute_ID,
--Положение на начальном сегменте
--NotNull=1  FixedAlias=1  Type='number(15,3)'
	FromRoute_Measure,
--Конечный сегмент (маршрута Route)
--NotNull=1  FixedAlias=1  Type=ppmIdType
	ToRoute_ID,
--Положение на конечном сегменте
--NotNull=1  FixedAlias=1  Type='number(15,3)'
	ToRoute_Measure
;


--LookupTableTemplate='CL'
--TemplateDescription='Сгенерированный по шаблону ''CL'' справочник кодовых значений для показателя {0}'
SELECT
--Кодовое мнемоническое обозначение элемента справочника, должно быть уникальным в пределах справочника
--PK=1   Type=ppmStr&'(75)'   InitValues={'Unknown','VerifiedUnknown'}
	code  CL,
--PODS7: A precise statement of the nature, properties, scope, or essential qualities of the concept
--NotNull=1   Type=ppmStr&'(255)'   InitValues={'Не определено','Не может быть определено'}
	Description  NAME,
--PODS7: An enumerated value that represents that life cycle status of a code list value.
--FixedAlias=1  Type=ppmStr&'(50)'
	status  STATUS_CODE,
--PODS7: Descriptive text that further details the life cycle status of a code list value.
--FixedAlias=1  Type=ppmStr&'(255)'
	comments  STATUS_COMMENTS,
--PODS7: Code that has been superseded by the code.
--FixedAlias=1   Type=ppmStr&'(75)'
	supersedes  PREV_CODE
WHERE status IS NULL
;

--Pipelines
--Трубопроводы
--Substance='Pipe'
SELECT
--Inherits='TableBase'
--Inherits='History'
--Inherits='Audit'
--Inherits='Named'
--Inherits='Describe'

--Назначение трубопровода (как в OIS Pipe)
	Purpose_CL,
--Тип трубопровода (как в OIS Pipe)
	Type_CL,
--Тип трубопроводной сети (как в OIS Pipe)
	Network_CL,
--PODS7: Indicates if the pipeline record is considered part of a transmission, distribution or gathering network of pipelines.
	SysType_CL,
--Ссылка на "родительский" трубопровод
--Type=ppmIdType
	Parent_ID

--Можно ли запускать скребок
--	,Piggable_CL
--Можно ли запускать умный скребок
--	,Smartpiggable_CL
--
--NotNull=1
--	Location_CL
--
--	,PIPELINE_ORDER
--	,PIPELINE_TAG
--	,OPERATIONAL_STATUS
--

--	,IS_REGULATED
--

--	,HAS_ROUTE
--	,IS_LOW_FLOW
--	,HAS_LRS
--
FROM Pipe
;

--RoutesNetworks
--Сеть маршрутов трубопровода (аналог "участка трубопровода" OIS Pipe)
--Substance='Routesnet'
SELECT
--Inherits='TableBase'
--Inherits='History'
--Inherits='Audit'
--Inherits='Describe'
--Inherits='Geometry'
--FixedAlias=1  NotNull=1
	Pipe_ID
FROM RoutesNet
;

--Routes
--Сегмент маршрута трубопровода (аналог "простого участка" OIS Pipe)
--Substance='Route'
SELECT
--Inherits='TableBase'
--Inherits='History'
--Inherits='Audit'
--FixedAlias=1  NotNull=1
	Routesnet_ID
FROM Route
;

--Markers
--Некая точка на местности или трубе
--Substance='Marker'
SELECT
--Inherits='TableBase'
--Inherits='History'
--Inherits='Audit'
--Inherits='Named'
--Inherits='Geometry'
--Inherits='LrsPoint'

--Смещение от осевой линии трубы (если маркер привязан к трубе)
--Type='number(15,2)'
	Offset_Measure,
--Тип маркера ("pipeline", "milepost", "above ground" и т.п.)
	Type_CL
FROM Marker
;


--Joints
--Логическое соединение труб между собой, начальная и конечная точка в LRS-координатах. Для полноценной связи создаётся 2 записи
--Substance='Joint'
SELECT
--Inherits='TableBase'
--Inherits='History'
--Inherits='Audit'
--Inherits='TwoLrsPoints'

--Cеть маршрутов, к которой относится начальная точка 
--NotNull=1  FixedAlias=1  Type=ppmIdType
	FromRoutesnet_ID,
--Cеть маршрутов, к которой относится конечная точка 
--NotNull=1  FixedAlias=1  Type=ppmIdType
	ToRoutesnet_ID
--
FROM Joint
;

