--AbstractTable='TableBase'
SELECT
--FixedAlias=1
--Уникальный ID записи
	ROW_ID,
--ID сущности, свойства которой описывает запись
	ID
;

--AbstractTable='History'
SELECT
--Начальные дата/время истинности факта
--NotNull=1
--FixedAlias=1
	from_date  AS START_TIME,
--Конечные дата/время истинности факта (NULL соответствует истинности по настоящее время)
--FixedAlias=1
	to_date  AS END_TIME
;

--AbstractTable='Describe'
SELECT
--Дополнительное описание свойства сущности, описываемого в текущей записи БД
	Description,
--Дополнительный комментарий в свободной форме, заполняемый оператором, поставщиком или другим участником бизнеса
	Comments
;

--AbstractTable='Audit'
SELECT
--Дата/время редактирования записи
	Edit_Time,
--Пользователь, отредактировавший запись
	Editor_User,
--Дата/время создания записи
--NotNull=1
	Create_Time,
--Пользователь, создавший запись
--NotNull=1
	Creator_User
;

--LookupTableTemplate='CL'
--TemplateDescription='Сгенерированный по шаблону ''CL'' справочник кодовых значений для показателя {0}'
SELECT
--Кодовое мнемоническое обозначение элемента справочника, должно быть уникальным в пределах справочника
--NotNull=1   InitValues={'Unknown','VerifiedUnknown'}
	code  _CODE,
--PODS7: A precise statement of the nature, properties, scope, or essential qualities of the concept
--NotNull=1   InitValues={'Не определено','Не может быть определено'}
	Description  _NAME,
--PODS7: An enumerated value that represents that life cycle status of a code list value.
--FixedAlias=1
	status  STATUS_CODE,
--PODS7: Descriptive text that further details the life cycle status of a code list value.
--FixedAlias=1
	comments  STATUS_COMMENTS,
--PODS7: Code that has been superseded by the code.
--FixedAlias=1
	supersedes  PREV_CODE
WHERE status IS NULL
;

--Pipelines
--Трубопроводы
--Substance='Pipe'
SELECT
--Inherits='TableBase'

--Inherits='History'
	Name
	,Location_CL

--	,PIPELINE_ORDER
--	,PIPELINE_TAG
--	,OPERATIONAL_STATUS

	,Piggable_CL

--	,IS_REGULATED

	,Smartpiggable_CL
	,Type_CL
--	,HAS_ROUTE
--	,IS_LOW_FLOW
--	,HAS_LRS

	,SysType_CL
--	,PARENT_PIPELINE_ID

--Inherits='Audit'
--Inherits='Describe'

FROM PIPELINE
;

