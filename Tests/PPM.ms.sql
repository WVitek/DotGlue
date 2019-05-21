--AbstractHistory
--AbstractTable='History'
SELECT
-- fixedAlias=1
	from_date  AS START_TIME,
-- fixedAlias=1
	to_date  AS END_TIME
;

--AbstractDescription
--AbstractTable='Describe'
SELECT
	Description,
	Comments
;

--AbstractAudit
--AbstractTable='Audit'
SELECT
	Edit_Time,
	Editor_User,
	Create_Time,
	Creator_User
;

--CodeLookupTemplate
--LookupTableTemplate='CL'
SELECT
	code  _CODE,
	Description  _NAME,
-- fixedAlias=1
	status  STATUS_CODE,
-- fixedAlias=1
	comments  STATUS_COMMENTS,
-- fixedAlias=1
	supersedes  PREV_CODE
WHERE status IS NULL
;

--Pipelines
--Трубопроводы
--Substance='Pipe'
SELECT
	ID
--	,FROM_DATE  AS START_TIME
--	,TO_DATE  AS END_TIME

--Inherits='History'
	,Name
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

--	,CREATE_DATE  AS Pipeline_CreaTime
--	,EDIT_DATE  AS Pipeline_EdiTime
--	,CREATOR  AS Pipeline_CREATOR
--	,EDITOR  AS Pipeline_EDITOR

--Inherits='Describe'

--	,DESCRIPTION  AS Pipeline_DESCRIPTION
--	,COMMENTS
--	,STATUS
--	,PRESERVE_RELATE_ID
FROM PIPELINE
;

