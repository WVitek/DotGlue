
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
--NotNull=1  FixedAlias=1  Type=ppmTime
	from_date  AS START_TIME,
--Время окончания периода истинности факта (NULL равносильно истинности по настоящее время)
--FixedAlias=1  Type=ppmTime
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
--Type=ppmTime
	Edit_Time,
--Пользователь, отредактировавший запись
--Type=ppmStr&'(50)'
	Editor_User,
--Время создания записи
--NotNull=1  Type=ppmTime
	Create_Time,
--Пользователь, создавший запись
--NotNull=1  Type=ppmStr&'(50)'
	Creator_User
;

--AbstractTable='LrsSectionFeature'
--Линейный объект или событие на трубопроводе (отрезок в LRS-координатах)
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

--AbstractTable='AssetCommon'
--Набор общих полей для всех таблиц измерений по ассетам (имуществу), должен идти сразу после ID
SELECT
--Inherits='LrsSectionFeature'

--Дата/время изготовления
--Type=ppmTime
	Manufact_Date AS Manufact_TIME,
--Дата/время монтажа/установки/нанесения
--Type=ppmTime
	Install_Date AS Install_TIME,

--Производитель
	Manufacturer  AS Manuf_RD,
--Материал
	Material  AS Material_RD,
--Спецификация / технические условия / ГОСТ
	Specification  AS Spec_RD
;

---------------------------- Шаблоны справочников -----------------------------


--LookupTableTemplate='RD'
--RD от Reference Data - справочные данные
----TemplateDescription="Сгенерированный по шаблону 'RD' справочник кодовых значений для '{0}'"
SELECT
--Кодовое мнемоническое обозначение элемента справочника, должно быть уникальным в пределах справочника
--PK=1   Type=ppmStr&'(75)'   InitValues={'Unknown','VerifiedUnknown'}
	Code  AS RD,

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
	ReplacedWith_Code
;

--LookupTableTemplate='HRD'
--HRD от Hierarchical Reference Data - иерархические справочные данные
----TemplateDescription="Сгенерированный по шаблону 'HRD' справочник допустимых символьных значений для '{0}'"
SELECT
--Кодовое мнемоническое обозначение элемента справочника, должно быть уникальным в пределах справочника
--PK=1   Type=ppmStr&'(75)'  InitValues={'Unknown','VerifiedUnknown'}
	Code  AS HRD,

--Служебное псевдополе - отделяет "входные" поля от "выходных"
	0	INS_OUTS_SEPARATOR,

--PODS7: A precise statement of the nature, properties, scope, or essential qualities of the concept
--NotNull=1   Type=ppmStr&'(255)'   InitValues={'Не определено','Не может быть определено'}
	Description  AS DESCR,

--Обозначение иерархического уровня (н-р, Компания/ДО/НГДУ/цех; Месторождение/площадь/купол; и т.п.)
	Level  AS Level_RD,
--PODS7: An enumerated value that represents that life cycle status of a code list value.
--FixedAlias=1  Type=ppmStr&'(50)'
	Status  LookupEntryStatus_ID,
--PODS7: Descriptive text that further details the life cycle status of a code list value.
--FixedAlias=1  Type=ppmStr&'(255)'
	Comments  LookupEntry_COMMENTS,
--Кодовая строка подменена новой (например, в ходе "очистки" импортированных данных)
--FixedAlias=1   Type=ppmStr&'(75)'
	ReplacedWith_Code,
--Код родительского элемента для организации иерархии кодов
--FixedAlias=1   Type=ppmStr&'(75)'
	Parent_Code
;

--LookupTableTemplate='RC'
--RC от Reference Caliber - эталонный калибр, диаметр, масштаб
----TemplateDescription='Сгенерированный по шаблону ''RC'' справочник допустимых числовых значений для ''{0}'''
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

--Pipe_PKs
--Перечень всех сущностей-трубопроводов, факты по которым имеются в БД
--Substance='Pipe'
SELECT
--PK=1  Type=ppmIdType
	Pipe_ID
FROM Pipe_PK
;

--Pipelines
--Трубопроводы
--Substance='Pipe'
SELECT
--NotNull=1  Type=ppmIdType
	Pipe_ID,
--Inherits='History'
--Inherits='TableBase'
--Inherits='Audit'
--Inherits='Named'
--Inherits='Describe'

--Назначение трубопровода (как в OIS Pipe)
	Purpose  AS Purpose_RD,
--Тип трубопровода (как в OIS Pipe)
	Type  AS Type_RD,
--Тип трубопроводной сети (как в OIS Pipe)
	Network  AS Network_RD,
--PODS7: Indicates if the pipeline record is considered part of a transmission, distribution or gathering network of pipelines.
	SysType  AS SysType_RD,
--Ссылка на "родительский" трубопровод
--Type=ppmIdType
	Parent_ID,
--Какое месторождение обслуживает трубопровод
--FixedAlias=1
	Oilfield  AS Oilfield_RD,
--Код уровня трубопровода в иерархии
	Level_RD
--Inherits='Geometry'
FROM Pipe
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
	Pipe_Measure,

--Смещение от осевой линии трубы (если маркер привязан к трубе)
--Type='numeric(15,3)'
	Offset_Measure,
--Тип маркера ("pipeline", "milepost", "above ground" и т.п.)
	Type  AS Type_RD
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
	Operator  AS Operator_HRD
FROM PipeOperator
;

--PipeSegment
--Ассет: Трубный сегмент
--Substance='PipeSeg'
SELECT
--Inherits='AssetCommon'

--Марка стали (или предел прочности на разрыв)
	Grade  AS Grade_RD,
--Тип соединения цельных секций трубы в сегменте (например: кольцевая сварка; винтовое соединение; ...)
	JoinType  AS JoinType_RD,
--Номинальный диаметр
	NominalDiam  AS Nominal_Diameter__RC,
--Происхождение (например: изначально установленная; заменённая)
	Origin  AS Origin_RD,
--Толщина стенки
	WallThickness  AS Wall_Thickness__RC,
--Наружный диаметр
	OuterDiameter  AS PipeSeg_OuterDiam__RC,
--Тип трубы (например: труба (обычная); защитный кожух системы "труба в трубе"; изгиб и т.п.)
	Type  AS Type_RD
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
	ApplDoer  AS ApplDoer_RD,
--Место, в котором производилось нанесение покрытия
	ApplSite  AS ApplSite_RD,
--Метод/способ нанесения покрытия
	ApplMethod  AS ApplMethod_RD,
--Цель нанесения покрытия
--NotNull=1
	ApplPurpose  AS ApplPurpose_RD,
--Номер слоя покрытия. Отрицательные значения для внутреннего покрытия, положительные - для наружнего.
--Type='numeric(2)'
	Layer_Number,
--Type='numeric(3,1)'
	Thickness
FROM Coating
;

--Elbows
--Ассет: Трубное колено // фасонная деталь для изменения направления продольной оси трубопровода
--Substance='Elbow'
SELECT
--Inherits='AssetCommon'

--Радиус осевой линии трубного колена.
--Обычно радиус примерно равен полутора наружным диаметрам (по ГОСТ-17380—2001 "ОТВОДЫ КРУТОИЗОГНУТЫЕ ТИПА 3D (R ≈ 1,5 DN)")
--Type='numeric(15,3)'
	Radius,
--Марка стали колена (или предел прочности на разрыв)
	Grade  AS Grade_RD,
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


--Valves
--Ассет: запорная арматура - клапан/вентиль/задвижка/кран
--Substance='Valve'
SELECT
--Inherits='AssetCommon'

--Изготовитель привода/устройства управления
	Operator_Manufacturer  AS OperatorManuf_RD,
--Тип привода/устройства управления
	Operator_Type  AS OperatorType_RD,
--Наружный диаметр
	Outside_Diameter  AS Valve_OuterDiam__RC,
--PODS7: Indicates the type of valve - ball, gate, piston etc.
--This attribute is a physical description of the valve configuration and has nothing to do with the function or purpose of the valve.
	Type  AS ValveType_RD,
--The system function or the main purpose of the valve: blow-off, check, emergency shutdown, isolation, maintenance, meter, or pigging.
	Func  AS ValveFunc_RD
FROM Valve
;

--Accidents
--Записи об авариях на трубопроводах
--Substance='Acdt'
SELECT
--Inherits='LrsSectionFeature'

--Pipe:ID комиссии
--FixedAlias=1
	Cmte_ID,

--Pipe:ID ремонта
--FixedAlias=1
	Repair_ID,

--Pipe:Адрес от начала трубопровода
	FromPipeBeg_Measure,
--Pipe:Вид аварии
	Type_RD,
--Pipe:Вид и форма дефекта
	Defect_DESCR,
--Pipe:Виновные в отказе
	GuiltyPersons_DESCR,
--Pipe:Выводы
	Conclusion_DESCR,
--Pipe:Газовый фактор
--Type='numeric(15,8)'
	OilGas_Ratio,
--Pipe:Дата аварии
	TIME,
--Pipe:Дата возобновления перекачки
	Restart_TIME,
--Pipe:Дата и список перекр задвиж
	ValvesClosing_DESCR,
--Pipe:Дата конца вскрытия участка
	OpeningEnd_TIME,
--Pipe:Дата конца ликвидации
	ElimEnd_TIME,
--Pipe:Дата ликвид последствий
	ConseqElimEnd_TIME,
--Pipe:Дата начала вскрытия участка
	OpeningBeg_TIME,
--Pipe:Дата начала ликвидации
	ElimBeg_TIME,
--Pipe:Дата остановки перекачки
	Stop_TIME,
--Pipe:Дата сообщения в органы
	Notif_TIME,
--Pipe:Дебит остановленных скв
--Type='numeric(15,3)'
	StoppedWells_VolRate,
--Pipe:Детализация обьекта
	FailedObjDetail_RD,
--Pipe:Детализация причины
	Reason_RD,
--Pipe:Диаметр отверстия
	Hole_Diameter,
--Pipe:Длина отверстия
--Type='numeric(15,3)'
	Hole_Length,
--Pipe:Затраты на ликвид аварии
--Type='numeric(15,3)'
	ElimCost_Money,
--Pipe:Категория аварии
	Category_RD,
--Pipe:Кинем вязкость жидкости
--Type='numeric(15,8)'
	Fluid_KinemVisc,
--Pipe:Количество пролитой воды
--Type='numeric(15,3)'
	WaterLeak_Volume,
--Pipe:Количество разлитой нефти
	OilLeak_Volume,
--Pipe:Количество убранной нефти
	GatheredOil_Volume,
--Pipe:Количество утечки газа
	GasLeak_Volume,
--Pipe:Мероприятия по ликвидации
	ActivityPlan_DESCR,
--Pipe:Место аварии
	Site_DESCR,
--Pipe:Место аварии по часам
	HoleClkwsAngle_RD,
--Pipe:Недобор газа
	GasLoss_Volume,
--Pipe:Недобор газоконденсата
	GascondLoss_Volume,
--Pipe:Номер загрязненного участка
	ContamSiteNum_DESCR,
--Pipe:Обводненность
--Type='numeric(8,5)'
	Watercut,
--Pipe:Обноруживший отказ
	WhoDetected_RD,
--Pipe:Обьект аварии
	FailedObj_RD,
--Pipe:Остановлено скважин
--Type='numeric(9)'
	StoppedWells_Count,
--Pipe:Ответственные
	RespPersons_DESCR,
--Pipe:Плотность жидкости
--Type='numeric(15,8)'
	Fluid_Density,
--Pipe:Плотность нефти
	Oil_Density,
--Pipe:Площадь загрязнения
--Type='numeric(15,3)'
	Contam_Area,
--Pipe:Площадь рекульт земель
	Reclaim_Area,
--Pipe:Попадание в водный объект
	WaterContam_RD,
--Pipe:Потери в закачке
	InjLoss_Volume,
--Pipe:Потери нефти в добыче
	ProdLoss_Volume,
--Pipe:Признак аварии
	Mark_RD,
--Pipe:Причина аварии
	Cause_RD,
--Pipe:Проверен
	IsChecked_RD,
--Pipe:Прямые финансовые потери
	ActualDamage_Money,
--Pipe:Р в момент отказа
--Type='numeric(15,3)'
	Time_Pressure,
--Pipe:Расстояние от сварного шва
	ToWeldJoint_Measure,
--Pipe:Расход жидкости
	Fluid_VolRate,
--Pipe:Расход нефти
	Oil_VolRate,
--Pipe:Регистрационный номер
	Reg_CODE,
--Pipe:Скорость потока
--Type='numeric(15,7)'
	Fluid_Speed,
--Pipe:Состояние внутр покрытия
	InnerCoat_DESCR,
--Pipe:Состояние изоляции
	Insul_DESCR,
--Pipe:Способ ликвидации СНГ
	RepairType_RD,
--Pipe:Способ обнаружения
	DetectType_RD,
--Pipe:Температура потока
--Type='numeric(15,3)'
	Fluid_Temperature,
--Pipe:Тип места отказа
	SiteType_RD,
--Pipe:Уровень техногенного события
	DangerLevel_RD,
--Pipe:Установлен хомут
	IsClampInstalled_RD,
--Pipe:Ущерб
	Damage_Money,
--Pipe:Характеристика загр участка
	ContamSoilType_RD,
--Pipe:Ширина отверстия
--Type='numeric(15,3)'
	Hole_Width,
--Pipe:Штраф
	ContamPenalty_Money
FROM Accident
;

--CrossingHydrology
--Пересечения с гидрологическими объектами 
--Substance='XHydr'
SELECT
--Inherits='LrsSectionFeature'

------Название пересечения с гидрологическим объектом (хранится в Description)
----	Name,

--Тип пересекаемого гидрологического объекта
	Type_RD,
--Pipe: Судоходность (гидрологического объекта)
	Navigable_RD,
--Угол пересечения в градусах
	Angle,
--Pipe: Способ прокладки перехода (через гидрологический объект, тип конструкции)
	LayingMethod_RD,
--Pipe: Ширина русла в межень (при низком сезонном уровне воды)
	BedLow_Width,
--Pipe: Диаметр перехода (если есть)
	Outer_Diameter,
--Pipe: Толщина стенки перехода (если есть)
	Wall_Thickness,
--Pipe: Глубина перехода
--Type='numeric(15,3)'
	Depth,
--Pipe: Наличие байпаса
	WithBypass_RD,
--Pipe: Категория перехода
	Class_RD,
--Наименование гидрологического объекта (Pipe: Название реки)
--FixedAlias=1
	Hydrology_RD,
--Pipe: Скорость течения, м/с
--Type='numeric(15,3)'
	Flow_Velocity,
--Pipe: Глубина (гидрологического объекта в месте пересечения)
	Hydrology_Depth,
--Pipe: Толщина стенки отбраковочная
	WallMin_Thickness,
--Pipe: Норматив НДС (норматив допустимых сбросов)
	MaxDisch_RD,
--Pipe: Дата ввода в эксплуатацию
	Comiss_Date  AS Comiss_Time
--
FROM CrossingHydrology
;