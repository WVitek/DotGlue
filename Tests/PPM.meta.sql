
---------------- Абстрактные таблицы (наследуемые списки полей) ----------------


--AbstractTable='TableBase'
SELECT
--Уникальный ID записи (у логической сущности отдельный ID?)
--PK=1  Type=ppmIdType  FixedAlias=1
	ID	 AS Row_ID
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

--AbstractTable='LrsPointFeature'
--Точечный объект на трубопроводе (точка в LRS-координатах)
SELECT
--Расположено на трубопроводе
--NotNull=1 FixedAlias=1  Type=ppmIdType
	Pipe_ID,
--Inherits='History'
--Inherits='TableBase'
--Inherits='Audit'
--Расстояние от начала трубопровода
--FixedAlias=1  Type='numeric(15,3)'
	Pipe_Measure
;

--AbstractTable='LrsSectionFeature'
--Линейный объект на трубопроводе (отрезок в LRS-координатах)
SELECT
--Расположено на трубопроводе
--NotNull=1 FixedAlias=1  Type=ppmIdType
	Pipe_ID,
--Inherits='History'
--Inherits='TableBase'
--Inherits='Audit'
--Расстояние начала отрезка от начала трубы
--NotNull=1  FixedAlias=1  Type='numeric(15,3)'
	FromPipe_Measure,
--Расстояние конца отрезка от начала трубы
--NotNull=1  FixedAlias=1  Type='numeric(15,3)'
	ToPipe_Measure
;


--AbstractTable='AssetTimes'
--Даты изготовления/монтажа имущества
SELECT
--Дата/время изготовления
--Type='date'
	Manufact_Date AS Manufact_TIME,
--Дата/время монтажа/установки/нанесения
--Type='date'
	Install_Date AS Install_TIME
;


---------------------------- Шаблоны справочников -----------------------------


--LookupTableTemplate='RD'
--TemplateDescription='Сгенерированный по шаблону ''RD'' справочник кодовых значений для показателя {0}'
SELECT
--Кодовое мнемоническое обозначение элемента справочника, должно быть уникальным в пределах справочника
--PK=1   Type=ppmStr&'(75)'   InitValues={'Unknown','VerifiedUnknown'}
	Code  RD,
--PODS7: A precise statement of the nature, properties, scope, or essential qualities of the concept
--NotNull=1   Type=ppmStr&'(255)'   InitValues={'Не определено','Не может быть определено'}
	Description  NAME,
--PODS7: An enumerated value that represents that life cycle status of a code list value.
--FixedAlias=1  Type=ppmStr&'(50)'
	Status  STATUS_CODE,
--PODS7: Descriptive text that further details the life cycle status of a code list value.
--FixedAlias=1  Type=ppmStr&'(255)'
	Comments  STATUS_COMMENTS,
--PODS7: Code that has been superseded by the code.
--FixedAlias=1   Type=ppmStr&'(75)'
	Supersedes  PREV_CODE
;

--LookupTableTemplate='HRD'
--TemplateDescription='Сгенерированный по шаблону ''HRD'' справочник кодовых значений для показателя {0}'
SELECT
--Кодовое мнемоническое обозначение элемента справочника, должно быть уникальным в пределах справочника
--PK=1   Type=ppmStr&'(75)'   InitValues={'Unknown','VerifiedUnknown'}
	Code  HRD,
--PODS7: A precise statement of the nature, properties, scope, or essential qualities of the concept
--NotNull=1   Type=ppmStr&'(255)'   InitValues={'Не определено','Не может быть определено'}
	Description  NAME,
--Обозначение иерархического уровня (н-р, Компания/ДО/НГДУ/цех; Месторождение/площадь/купол; и т.п.)
	Level_RD,
--PODS7: An enumerated value that represents that life cycle status of a code list value.
--FixedAlias=1  Type=ppmStr&'(50)'
	Status  STATUS_CODE,
--PODS7: Descriptive text that further details the life cycle status of a code list value.
--FixedAlias=1  Type=ppmStr&'(255)'
	Comments  STATUS_COMMENTS,
--PODS7: Code that has been superseded by the code.
--FixedAlias=1   Type=ppmStr&'(75)'
	Supersedes  PREV_CODE,
--Код родительского элемента для организации иерархии кодов
--Type=ppmStr&'(75)'
	Parent_CODE
;


------------------------------- Таблицы фактов --------------------------------


--Pipelines
--Трубопроводы
--Substance='Pipe'
SELECT
--NotNull=1
	Pipe_ID,
--Inherits='History'
--Inherits='TableBase'
--Inherits='Audit'
--Inherits='Named'
--Inherits='Describe'

--FixedAlias=1
	Route_ID,
--Назначение трубопровода (как в OIS Pipe)
	Purpose_RD,
--Тип трубопровода (как в OIS Pipe)
	Type_RD,
--Тип трубопроводной сети (как в OIS Pipe)
	Network_RD,
--PODS7: Indicates if the pipeline record is considered part of a transmission, distribution or gathering network of pipelines.
	SysType_RD,
--Ссылка на "родительский" трубопровод
--Type=ppmIdType
	Parent_ID,
--Какое месторождение обслуживает трубопровод
--Lookup='Oilfield_RD'
	Oilfield_RD,
--Код уровня трубопровода в иерархии
	Level_RD
FROM Pipe
;


--Routes
--Маршруты (геометрия трубопроводов)
--Substance='Route'
SELECT
--NotNull=1
	Route_ID
--Inherits='History'
--Inherits='TableBase'
--Inherits='Audit'
--Inherits='Geometry'
FROM Route
;

--Markers
--Некая точка на местности или трубе
--Substance='Marker'
SELECT
--NotNull=1
	Marker_ID,
--Inherits='History'
--Inherits='TableBase'
--Inherits='Audit'
--Inherits='Named'
--Inherits='Geometry'

--Опциональная принадлежность трубопроводу
--FixedAlias=1  Type=ppmIdType
	Pipe_ID,
--Расстояние от начала трубопровода
--FixedAlias=1  Type='numeric(15,3)'
	Pipe_Measure

--Смещение от осевой линии трубы (если маркер привязан к трубе)
--Type='numeric(15,3)'
	Offset_Measure,
--Тип маркера ("pipeline", "milepost", "above ground" и т.п.)
	Type_RD
FROM Marker
;


--Joints
--Логическое соединение труб между собой, начальная и конечная точка в LRS-координатах. Для полноценной связи можно создавать 2 записи
--Substance='Joint'
SELECT
--NotNull=1
	Joint_ID,
--Inherits='History'
--Inherits='TableBase'
--Inherits='Audit'
--Ассет (если есть), с которым ассоциировано соединение
	Asset_ID,
--Труба, к которой относится начальная точка соединения
--NotNull=1  FixedAlias=1  Type=ppmIdType
	FromPipe_ID,
--Расстояние начальной точки от начала трубы
--NotNull=1  FixedAlias=1  Type='numeric(15,3)'
	FromPipe_Measure,
--Труба, к которой относится конечная точка соединения
--NotNull=1  FixedAlias=1  Type=ppmIdType
	ToPipe_ID,
--Расстояние конечной точки от начала трубы
--NotNull=1  FixedAlias=1  Type='numeric(15,3)'
	ToPipe_Measure
FROM Joint
;

--Coatings
--Покрытия трубопровода
--Substance='Coating'
SELECT
--Inherits='LrsSectionFeature'
--Исполнитель операции нанесения покрытия
	ApplDoer_RD,
--Место, в котором производилось нанесение покрытия
	ApplSite_RD,
--Метод/способ нанесения покрытия
	ApplMethod_RD,
--Цель нанесения покрытия
	ApplPurpose_RD,
--Номер слоя покрытия. Отрицательные значения для внутреннего покрытия, положительные - для наружнего.
--Type='numeric(2)'
	Layer_Number,
--Тип покрытия (классификатор)
--NotNull=1
	Type_RD,
--Материал использованный при изготовлении покрытия
--NotNull=1
	Matherial_RD,
--Type='numeric(3,1)'
	Thickness
--Inherits='AssetTimes'
FROM Coating
;

--PipeOperators
--Эксплуатирующее подразделение
--Substance='PipeOperator'
SELECT
--Inherits='LrsSectionFeature'
	HRD
FROM PipeOperator
;

--Pipe
