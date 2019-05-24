--AbstractTable='TableBase'
SELECT
--Уникальный ID записи
--PK=1  FixedAlias=1  Type=ppmIdType
	ROW_ID,

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
--Inherits='Named'

--NotNull=1
	Location_CL

--	,PIPELINE_ORDER
--	,PIPELINE_TAG
--	,OPERATIONAL_STATUS
--
	,Piggable_CL

--	,IS_REGULATED
--
	,Smartpiggable_CL
	,Type_CL
--	,HAS_ROUTE
--	,IS_LOW_FLOW
--	,HAS_LRS
--
	,SysType_CL
--	,PARENT_PIPELINE_ID
--
--Inherits='Audit'
--Inherits='Describe'

FROM Pipeline
;

