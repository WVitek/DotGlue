
---------------- Абстрактные таблицы (наследуемые списки полей) ----------------


--AbstractTable='TableBase'
SELECT
--Уникальный ID записи (у логической сущности отдельный ID?)
--PK=1  Type=ppmIdType
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
	Description  AS DESCR,
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

------AbstractTable='LrsPointFeature'
------Точечный объект на трубопроводе (точка в LRS-координатах)
----SELECT
------Расположено на трубопроводе
------NotNull=1 FixedAlias=1  Type=ppmIdType
----	Pipe_ID,
------Inherits='History'
------Inherits='TableBase'
------Inherits='Audit'
------Расстояние от начала трубопровода
------FixedAlias=1  Type='numeric(15,3)'
----	Pipe_Measure
----;

--AbstractTable='LrsSectionFeature'
--Линейный объект на трубопроводе (отрезок в LRS-координатах)
SELECT
--Расположено на трубопроводе
--NotNull=1 FixedAlias=1  Type=ppmIdType
	Pipe_ID,
--Inherits='History'
--Inherits='TableBase'
--Inherits='Audit'
--Inherits='Describe'
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

--AbstractTable='AssetCommon'
--Набор общих полей для всех таблиц измерений по ассетам (имуществу), должен идти сразу после ID
SELECT
--Служебное псевдополе - отделяет "входные" поля от "выходных"
	0	INS_OUTS_SEPARATOR,
--Производитель
	Manufacturer  AS Manuf_ID__RD,
--Материал
	Material  AS Material_ID__RD,
--Спецификация / технические условия
	Specification  AS Spec_ID__RD
;

---------------------------- Шаблоны справочников -----------------------------


--LookupTableTemplate='RD'
--RD от Reference Data - справочные данные
--TemplateDescription="Сгенерированный по шаблону 'RD' справочник кодовых значений для '{0}'"
SELECT
--Кодовое мнемоническое обозначение элемента справочника, должно быть уникальным в пределах справочника
--PK=1   Type=ppmStr&'(75)'   InitValues={'Unknown','VerifiedUnknown'}
	ID,

--Служебное псевдополе - отделяет "входные" поля от "выходных"
	0	INS_OUTS_SEPARATOR,

--PODS7: A precise statement of the nature, properties, scope, or essential qualities of the concept
--NotNull=1   Type=ppmStr&'(255)'   InitValues={'Не определено','Не может быть определено'}
	Description  AS DESCR,
--PODS7: An enumerated value that represents that life cycle status of a code list value.
--FixedAlias=1  Type=ppmStr&'(50)'
	Status  Status_CODE,
--PODS7: Descriptive text that further details the life cycle status of a code list value.
--FixedAlias=1  Type=ppmStr&'(255)'
	Comments  Status_COMMENTS,
--PODS7: Code that has been superseded by the code.
--FixedAlias=1   Type=ppmStr&'(75)'
	ReplacedWith_ID
;

--LookupTableTemplate='HRD'
--HRD от Hierarchical Reference Data - иерархические справочные данные
--TemplateDescription="Сгенерированный по шаблону 'HRD' справочник допустимых символьных значений для '{0}'"
SELECT
--Кодовое мнемоническое обозначение элемента справочника, должно быть уникальным в пределах справочника
--PK=1   Type=ppmStr&'(75)'  InitValues={'Unknown','VerifiedUnknown'}
	ID,

--Служебное псевдополе - отделяет "входные" поля от "выходных"
	0	INS_OUTS_SEPARATOR,

--PODS7: A precise statement of the nature, properties, scope, or essential qualities of the concept
--NotNull=1   Type=ppmStr&'(255)'   InitValues={'Не определено','Не может быть определено'}
	Description  AS DESCR,

--Обозначение иерархического уровня (н-р, Компания/ДО/НГДУ/цех; Месторождение/площадь/купол; и т.п.)
--FixedAlias=1  
	Level  AS Level_ID__RD,
--PODS7: An enumerated value that represents that life cycle status of a code list value.
--FixedAlias=1  Type=ppmStr&'(50)'
	Status  LookupEntryStatus_ID,
--PODS7: Descriptive text that further details the life cycle status of a code list value.
--FixedAlias=1  Type=ppmStr&'(255)'
	Comments  LookupEntry_COMMENTS,
--Кодовая строка подменена новой (например, в ходе "очистки" импортированных данных)
--FixedAlias=1   Type=ppmStr&'(75)'
	ReplacedWith_ID,
--Код родительского элемента для организации иерархии кодов
--FixedAlias=1   Type=ppmStr&'(75)'
	Parent_ID
;

--LookupTableTemplate='RC'
--RC от Reference Caliber - эталонный калибр, диаметр, масштаб
--TemplateDescription='Сгенерированный по шаблону ''RC'' справочник допустимых числовых значений для ''{0}'''
SELECT
--Числовое кодовое значение элемента справочника, должно быть уникальным в пределах справочника
--PK=1   Type='numeric(15,3)'
	Number,
--Доп. информация по данной записи справочника
--FixedAlias=1  Type=ppmStr&'(255)'
	Description  AS Number_DESCR,
--PODS7: An enumerated value that represents that life cycle status of a code list value.
--FixedAlias=1  Type=ppmStr&'(50)'
	Status  LookupEntryStatus_ID,
--PODS7: Descriptive text that further details the life cycle status of a code list value.
--FixedAlias=1  Type=ppmStr&'(255)'
	Comments  LookupEntry_COMMENTS,
--Элемент справочника заменяется другим
--FixedAlias=1   Type='numeric(15,3)'
	ReplacedWith_Number
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
	Purpose  AS Purpose_ID__RD,
--Тип трубопровода (как в OIS Pipe)
	Type  AS Type_ID__RD,
--Тип трубопроводной сети (как в OIS Pipe)
	Network  AS Network_ID__RD,
--PODS7: Indicates if the pipeline record is considered part of a transmission, distribution or gathering network of pipelines.
	SysType  AS SysType_ID__RD,
--Ссылка на "родительский" трубопровод
--Type=ppmIdType
	Parent_ID,
--Какое месторождение обслуживает трубопровод
----Lookup='Oilfield_ID'
	Oilfield  AS Oilfield_ID__RD,
--Код уровня трубопровода в иерархии
	Level_ID__RD
--Inherits='Geometry'
FROM Pipe
;


------Routes
------Маршруты (геометрия трубопроводов)
------Substance='Route'
----SELECT
------NotNull=1
----	Route_ID
------Inherits='History'
------Inherits='TableBase'
------Inherits='Audit'
------Inherits='Geometry'
----FROM Route
----;

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
	Type  AS Type_ID__RD
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

--PipeOperators
--Эксплуатирующее подразделение
--Substance='PipeOperator'
SELECT
--Inherits='LrsSectionFeature'
	Operator  AS Operator_ID__HRD
FROM PipeOperator
;

--PipeSegment
--Ассет: Трубный сегмент
--Substance='PipeSeg'
SELECT
--PK=1
	ID,
--Inherits='AssetCommon'

--Марка стали (или предел прочности на разрыв)
	Grade  AS Grade_ID__RD,
--Тип соединения цельных секций трубы в сегменте (например: кольцевая сварка; винтовое соединение; ...)
	JoinType  AS JoinType_ID__RD,
--Номинальный диаметр
	NominalDiam  AS Nominal_Diameter__RC,
--Происхождение (например: изначально установленная; заменённая)
	Origin  AS Origin_ID__RD,
--Толщина стенки
	WallThickness  AS Wall_Thickness__RC,
--Наружный диаметр
	OuterDiameter  AS PipeSeg_OuterDiam__RC,
--Тип трубы (например: труба (обычная); защитный кожух системы "труба в трубе"; изгиб и т.п.)
	Type  AS Type_ID__RD
FROM PipeSeg
;

--Coating
--Ассет: Покрытие трубы
--Substance='Coating'
SELECT
--PK=1
	ID,
--Inherits='AssetCommon'

--Исполнитель операции нанесения покрытия
	ApplDoer  AS ApplDoer_ID__RD,
--Место, в котором производилось нанесение покрытия
	ApplSite  AS ApplSite_ID__RD,
--Метод/способ нанесения покрытия
	ApplMethod  AS ApplMethod_ID__RD,
--Цель нанесения покрытия
	ApplPurpose  AS ApplPurpose_ID__RD,
--Номер слоя покрытия. Отрицательные значения для внутреннего покрытия, положительные - для наружнего.
--Type='numeric(2)'
	Layer_Number,
--Тип покрытия (классификатор)
--NotNull=1
	Type  AS Type_ID__RD,
--Type='numeric(3,1)'
	Thickness
FROM Coating
;

--Elbows
--Ассет: Трубное колено // фасонная деталь для изменения направления продольной оси трубопровода
--Substance='Elbow'
SELECT
--PK=1
	ID,
--Inherits='AssetCommon'

--Радиус осевой линии трубного колена.
--Обычно радиус примерно равен полутора наружным диаметрам (по ГОСТ-17380—2001 "ОТВОДЫ КРУТОИЗОГНУТЫЕ ТИПА 3D (R ≈ 1,5 DN)")
--Type='numeric(15,3)'
	Radius,
--Марка стали колена (или предел прочности на разрыв)
	Grade  AS Grade_ID__RD,
--Толщина стенки трубного колена
	Wall_Thickness__RC,
--Наружный диаметр на входе трубного колена
	Inlet_OuterDiam__RC,
--Наружний диаметр на выходе трубного колена
	Outlet_OuterDiam__RC,
--Угол изгиба направления трубопровода в вертикальной плоскости (вверх/вниз) // требуется уточнение положения плоскости
--Type='numeric(7,3)'
	Vert_Angle,
--Угол изгиба направления трубопровода в горизонтальной плоскости (вправо/влево) // требуется уточнение положения плоскости
--Type='numeric(7,3)'
	Horz_Angle
FROM Elbow
;

--PipeAssets
--Факты по "активам" (ассетам) трубопровода.
--Тут общие поля для всех ассетов + ссылки на таблицы детальных описаний
--Substance='PipeAsset'
SELECT
--Inherits='LrsSectionFeature'
--Inherits='AssetTimes'

--Тип ассета (для доп.классификации)
	Type as Type_ID__RD,
--FixedAlias=1
	PipeSeg_ID,
--FixedAlias=1
	Coating_ID,
--FixedAlias=1
	Elbow_ID
FROM PipeAsset
;

