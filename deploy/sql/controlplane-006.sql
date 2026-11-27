-- Regent Agent ControlPlane schema version 6.
-- Metadata-only migration: replace catalog comments and record version 6.
-- Unqualified quoted identifiers intentionally resolve through the caller's search_path.
-- Execute with the migration role; application startup never performs DDL.
BEGIN;

COMMENT ON EXTENSION "vector" IS
    '提供 vector 类型、距离算子与 HNSW 索引；仅承载 MemoryEmbeddings 的 256 维检索投影，不保存 claim 正文或替代权限过滤。';
COMMENT ON EXTENSION "ltree" IS
    '提供岗位层级路径、子树与深度查询；Positions.Path 的租户隔离仍由 TenantId 和 RLS 执行，路径本身不是授权边界。';

COMMENT ON TABLE "Approvals" IS
    '租户内高风险动作的审批聚合；请求、证据、有效期和终态共同约束执行，超时默认拒绝且不得静默改写历史。';
COMMENT ON COLUMN "Approvals"."Id" IS
    '审批记录标识；用于证据引用、审计和按标识定位，不承载租户或权限含义，也不表示存在行版本字段。';
COMMENT ON COLUMN "Approvals"."TenantId" IS
    '审批所属租户，是强制 RLS 的读写边界；必须与动作目标和请求主体处于同一租户。';
COMMENT ON COLUMN "Approvals"."ActionType" IS
    '待审批动作的稳定类型；执行层必须按该类型重新鉴权，不能仅凭审批存在放行。';
COMMENT ON COLUMN "Approvals"."Title" IS
    '供审批人识别的动作摘要；不得写入密钥、完整 Prompt 或其他敏感内容。';
COMMENT ON COLUMN "Approvals"."Impact" IS
    '动作影响范围和不可逆性说明，用于分级审批与二次确认。';
COMMENT ON COLUMN "Approvals"."Severity" IS
    '审批严重级别；决定升级路径和审批要求，不得由展示文案推断。';
COMMENT ON COLUMN "Approvals"."State" IS
    '审批状态机当前值；仅 Pending 可决策，终态不得回退。当前表无 Version 列，决策依赖应用状态条件、幂等记录和审计，而非数据库行版本。';
COMMENT ON COLUMN "Approvals"."Requester" IS
    '发起审批的主体引用；执行时仍须校验该主体的当前权限和租户。';
COMMENT ON COLUMN "Approvals"."CreatedAt" IS
    '审批创建的 UTC 时间，用于 SLA、审计排序和有效期判断。';
COMMENT ON COLUMN "Approvals"."ExpiresAt" IS
    '审批失效的 UTC 时间；过期后即使未显式拒绝也不得执行。';
COMMENT ON COLUMN "Approvals"."OptionsJson" IS
    '审批可选方案的结构化扩展快照；仅作决策证据，不替代权限或业务校验。';
COMMENT ON COLUMN "Approvals"."EvidenceJson" IS
    '支撑审批的脱敏证据引用；不得内联秘密、原始敏感日志或越权内容。';

COMMENT ON TABLE "Artifacts" IS
    'RustFS 对象的租户级元数据；ObjectKey 定位字节，Hash/Length/UploadState 用于确认 Ready 对象完整，不可覆盖和版本化由应用维护。';
COMMENT ON COLUMN "Artifacts"."Id" IS
    '对象元数据标识；同一对象内容更新应生成新记录而不是覆盖 Ready 版本。';
COMMENT ON COLUMN "Artifacts"."TenantId" IS
    '对象所属租户，是强制 RLS 边界；对象键前缀不能替代数据库租户校验。';
COMMENT ON COLUMN "Artifacts"."ProjectId" IS
    '关联项目标识；当前无数据库外键或项目删除级联，应用必须在写入、读取和清除时校验项目与租户，RustFS 对象按显式保留策略另行清理。';
COMMENT ON COLUMN "Artifacts"."TaskId" IS
    '产生或使用对象的任务标识；允许为空表示项目级成果。';
COMMENT ON COLUMN "Artifacts"."ObjectKey" IS
    'RustFS 中的对象键；由服务端按租户和资源范围生成，禁止把任意客户端路径当授权依据。';
COMMENT ON COLUMN "Artifacts"."Hash" IS
    '对象内容的校验摘要；上传完成和下载校验必须使用同一算法语义以防损坏或替换。';
COMMENT ON COLUMN "Artifacts"."ContentType" IS
    '对象的媒体类型，用于安全响应和后续处理，不决定其可信度。';
COMMENT ON COLUMN "Artifacts"."Length" IS
    '对象字节数；用于上传完整性、配额和下载限制，必须为非负值。';
COMMENT ON COLUMN "Artifacts"."UploadState" IS
    '上传状态机；只有 Ready 对象可被业务引用，PendingUpload 失败时需重试或清理孤儿对象。';
COMMENT ON COLUMN "Artifacts"."CreatedAt" IS
    '元数据创建的 UTC 时间，用于审计、清理和保留判断。';

COMMENT ON TABLE "AuditCheckpoints" IS
    '租户审计链的 Merkle 检查点；ThroughSeq 与 MerkleRoot 唯一对应一个已验证前缀，用于外部锚定和篡改检测。';
COMMENT ON COLUMN "AuditCheckpoints"."Id" IS
    '检查点记录标识；同一审计前缀重复生成必须保持内容一致或明确拒绝。';
COMMENT ON COLUMN "AuditCheckpoints"."TenantId" IS
    '检查点所属租户，是强制 RLS 边界；锚定和验证不得跨租户读取。';
COMMENT ON COLUMN "AuditCheckpoints"."ThroughSeq" IS
    '检查点覆盖的租户内最大审计序号；与 TenantId 共同唯一，必须指向已提交审计链前缀。';
COMMENT ON COLUMN "AuditCheckpoints"."MerkleRoot" IS
    '覆盖审计事件的 Merkle 根；用于验证链完整性和外部锚定，不能静默重算覆盖原值。';
COMMENT ON COLUMN "AuditCheckpoints"."Profile" IS
    '生成检查点时采用的审计/锚定配置版本，保证历史验证可复现。';
COMMENT ON COLUMN "AuditCheckpoints"."CreatedAt" IS
    '检查点生成的 UTC 时间；不得早于其覆盖的最后一条审计事件。';

COMMENT ON TABLE "AuditRecords" IS
    '租户内审计事件链；正常业务只追加，Seq、PreviousHash 与 Hash 形成可验证顺序，常规更新或删除会破坏完整性，历史缺哈希仅允许受控回填修复。';
COMMENT ON COLUMN "AuditRecords"."Id" IS
    '审计行的物理标识；用于存储定位，业务顺序和链校验以租户内 Seq 为准。';
COMMENT ON COLUMN "AuditRecords"."TenantId" IS
    '审计所属租户，是强制 RLS 边界；审计查询和导出必须保持租户隔离。';
COMMENT ON COLUMN "AuditRecords"."Seq" IS
    '租户内连续审计序号；与 TenantId 唯一，缺口或重复都必须由完整性校验发现。';
COMMENT ON COLUMN "AuditRecords"."EventType" IS
    '稳定审计事件类型；用于检索和完整性验证，不存储本地化展示文案。';
COMMENT ON COLUMN "AuditRecords"."Actor" IS
    '触发动作的主体引用；只保存审计所需标识，不内联凭据或完整身份资料。';
COMMENT ON COLUMN "AuditRecords"."PayloadJson" IS
    '事件的脱敏结构化证据；秘密、PII 和原始敏感内容必须在写入前移除或令牌化。';
COMMENT ON COLUMN "AuditRecords"."PreviousHash" IS
    '同一租户上一条审计记录的 Hash；Seq=1 的链首固定为空字符串而非 NULL，后续必须精确衔接，字段本身为 NOT NULL。';
COMMENT ON COLUMN "AuditRecords"."Hash" IS
    '由规范事件内容和 PreviousHash 计算的完整性摘要；常规业务不得更新，仅受控回填可补齐历史空值，用于发现篡改。';
COMMENT ON COLUMN "AuditRecords"."CreatedAt" IS
    '事件发生的 UTC 时间；迟到事件仍按所属周期入账，不能借写入时间改变业务归属。';

COMMENT ON TABLE "CandidateAppeals" IS
    '候选人对招聘决定或流程的申诉记录；理由、状态和处理结论保留复核依据，不能覆盖原评估。';
COMMENT ON COLUMN "CandidateAppeals"."Id" IS
    '申诉标识；用于复核、审计和幂等状态更新。';
COMMENT ON COLUMN "CandidateAppeals"."TenantId" IS
    '申诉所属租户，是强制 RLS 边界；候选人只能访问被授权的本租户记录。';
COMMENT ON COLUMN "CandidateAppeals"."CandidateId" IS
    '被申诉的候选人标识；必须指向同一租户的候选人。';
COMMENT ON COLUMN "CandidateAppeals"."Reason" IS
    '申诉理由；应最小化个人信息并保留必要事实，不得夹带秘密或无关敏感资料。';
COMMENT ON COLUMN "CandidateAppeals"."Status" IS
    '申诉处理状态；状态转换需留下审计，不能通过直接改字段跳过复核。';
COMMENT ON COLUMN "CandidateAppeals"."Resolution" IS
    '最终处理结论及依据；为空表示尚未闭环，填写后不得抹除原申诉。';

COMMENT ON TABLE "Candidates" IS
    '租户招聘候选人的盲评与流程投影；展示资料和评估证据必须按招聘权限最小化访问。';
COMMENT ON COLUMN "Candidates"."Id" IS
    '候选人记录标识；跨租户复用标识不得导致数据关联。';
COMMENT ON COLUMN "Candidates"."TenantId" IS
    '候选人所属租户，是强制 RLS 边界；招聘数据和申诉均不得跨租户查询。';
COMMENT ON COLUMN "Candidates"."PositionId" IS
    '应聘岗位标识；评分标准、JD 和录用决定必须绑定该岗位。';
COMMENT ON COLUMN "Candidates"."DisplayName" IS
    '招聘流程使用的最小化展示名称；属于个人数据，不得进入普通日志或遥测。';
COMMENT ON COLUMN "Candidates"."ModelVersion" IS
    '评估所用模型/规则的版本，用于偏差审计和结果复现。';
COMMENT ON COLUMN "Candidates"."JobDescription" IS
    '候选人应聘时采用的岗位说明快照；后续岗位文案更新不得改写历史评估依据。';
COMMENT ON COLUMN "Candidates"."Status" IS
    '招聘流程状态；录用、拒绝和终止必须遵循状态机并保留审计。';
COMMENT ON COLUMN "Candidates"."AssessmentJson" IS
    '结构化评估结果扩展；只能保存岗位相关能力证据，不得成为绕过能力门槛的入口。';
COMMENT ON COLUMN "Candidates"."FairnessJson" IS
    '盲评和公平性检查证据；应保存分组统计与复核状态，不保存无关个人特征。';
COMMENT ON COLUMN "Candidates"."CreatedAt" IS
    '候选人记录创建的 UTC 时间，用于流程审计和保留计算。';

COMMENT ON TABLE "Charters" IS
    '每租户一份的公司章程；TenantId 主键保证唯一，预算币种、约束和 Version 共同维持可审计配置。';
COMMENT ON COLUMN "Charters"."TenantId" IS
    '章程所属租户兼主键；每个租户最多一份当前章程，同时作为强制 RLS 边界。';
COMMENT ON COLUMN "Charters"."Mission" IS
    '公司使命与经营目标；用于启动和决策上下文，不构成额外权限。';
COMMENT ON COLUMN "Charters"."ConstraintsJson" IS
    '章程约束的结构化扩展；关键硬约束仍须由领域规则执行，不能只依赖 JSON 文案。';
COMMENT ON COLUMN "Charters"."MonthlyBudgetMinor" IS
    '月度预算的最小货币单位整数；不得使用浮点金额，和 Currency 一起解释。';
COMMENT ON COLUMN "Charters"."Currency" IS
    '月度预算对应的 ISO 4217 币种；不得从语言或地区推断，金额运算必须保持币种一致。';
COMMENT ON COLUMN "Charters"."GovernanceJson" IS
    '治理配置的结构化快照；不得包含秘密，也不能绕过审批、预算或合规硬控制。';
COMMENT ON COLUMN "Charters"."Version" IS
    '章程修订号；每次有效配置变更递增，用于审计和变更追踪，不单独构成数据库乐观锁。';
COMMENT ON COLUMN "Charters"."UpdatedAt" IS
    '最近一次有效更新的 UTC 时间；与 Version 一同用于变更追踪。';

COMMENT ON TABLE "Employees" IS
    '租户内独立 Agent 员工身份与运行状态；EmployeeNo 永久唯一，任职和权限由关联记录维护。';
COMMENT ON COLUMN "Employees"."Id" IS
    '员工运行时标识；用于任务、审计和任职关联，不等同于账户身份。';
COMMENT ON COLUMN "Employees"."TenantId" IS
    '员工所属租户，是强制 RLS 边界；跨租户任务不得引用该员工。';
COMMENT ON COLUMN "Employees"."EmployeeNo" IS
    '租户内永久员工工号；离职再入职保持同一编号，不得回收给其他员工。';
COMMENT ON COLUMN "Employees"."DisplayName" IS
    '员工展示名称；属于个人数据，遥测和错误响应必须脱敏。';
COMMENT ON COLUMN "Employees"."Position" IS
    '员工当前岗位的兼容展示字段；权威任职关系以 EmploymentPeriods 与 Positions 关联为准。';
COMMENT ON COLUMN "Employees"."State" IS
    '员工运行状态；terminated 等状态必须阻止新认领和旧 Token 访问。';
COMMENT ON COLUMN "Employees"."StateReason" IS
    '状态变更的脱敏理由；用于审计和恢复，不得保存秘密或完整敏感调查材料。';

COMMENT ON TABLE "EmploymentPeriods" IS
    '员工任职历史；StartedAt/EndedAt 定义连续任期，primary 任职与岗位槽位必须由应用在事务内保持唯一。';
COMMENT ON COLUMN "EmploymentPeriods"."Id" IS
    '任职周期标识；用于历史审计和幂等变更，不替代员工或岗位标识。';
COMMENT ON COLUMN "EmploymentPeriods"."TenantId" IS
    '任职所属租户，是强制 RLS 边界；员工和岗位必须属于同一租户。';
COMMENT ON COLUMN "EmploymentPeriods"."EmployeeId" IS
    '任职员工标识；同一员工可有多段历史，但同一时间只能有一个有效 primary 任职。';
COMMENT ON COLUMN "EmploymentPeriods"."PositionId" IS
    '任职岗位标识；主任职者槽位和岗位子树权限均依赖该关联。';
COMMENT ON COLUMN "EmploymentPeriods"."Kind" IS
    '任职类型，如 primary 或兼任；类型决定槽位和权限语义，必须使用稳定枚举值。';
COMMENT ON COLUMN "EmploymentPeriods"."StartedAt" IS
    '任职生效的 UTC 时间；不得晚于 EndedAt，用于历史回溯和审计。';
COMMENT ON COLUMN "EmploymentPeriods"."EndedAt" IS
    '任职结束的 UTC 时间；为空表示当前有效，结束任职必须同时释放槽位和派生权限。';

COMMENT ON TABLE "FairnessAudits" IS
    '岗位招聘公平性审计；样本量、选择率比和证据用于触发调查，不得自动降低能力门槛。';
COMMENT ON COLUMN "FairnessAudits"."Id" IS
    '公平审计标识；用于复查和审计追踪。';
COMMENT ON COLUMN "FairnessAudits"."TenantId" IS
    '审计所属租户，是强制 RLS 边界；招聘统计不得跨租户聚合。';
COMMENT ON COLUMN "FairnessAudits"."PositionId" IS
    '被审计岗位标识；审计口径必须与具体岗位 JD 和评估标准绑定。';
COMMENT ON COLUMN "FairnessAudits"."Period" IS
    '审计统计周期；使用稳定周期标识，保证迟到数据可按发生期归属。';
COMMENT ON COLUMN "FairnessAudits"."SampleSize" IS
    '纳入统计的候选样本数；过小样本必须标记证据不足，不能据此宣称无偏差。';
COMMENT ON COLUMN "FairnessAudits"."SelectionRateRatio" IS
    '分组选择率比；是调查信号而非自动处罚依据，解释时必须保留样本和口径。';
COMMENT ON COLUMN "FairnessAudits"."Status" IS
    '公平审计处理状态；异常必须进入复核并留下结论。';
COMMENT ON COLUMN "FairnessAudits"."DetailsJson" IS
    '分组统计和复核证据的结构化扩展；只保存必要聚合数据，不暴露无关个人特征。';

COMMENT ON TABLE "IdempotencyRecords" IS
    '租户内写操作的幂等重放记录；Key 与 Operation 唯一，RequestHash 区分同键不同请求并固定首次响应。';
COMMENT ON COLUMN "IdempotencyRecords"."Id" IS
    '幂等记录标识；用于内部追踪，不参与客户端幂等匹配。';
COMMENT ON COLUMN "IdempotencyRecords"."TenantId" IS
    '幂等记录所属租户，是强制 RLS 边界；不同租户可独立复用同一客户端键。';
COMMENT ON COLUMN "IdempotencyRecords"."Key" IS
    '客户端提供的幂等键；与 Operation、TenantId 共同确定一次逻辑写操作。';
COMMENT ON COLUMN "IdempotencyRecords"."Operation" IS
    '幂等键作用的稳定操作名；防止同一键在不同 API 或业务动作间误复用。';
COMMENT ON COLUMN "IdempotencyRecords"."ResponseJson" IS
    '首次完成时保存的脱敏响应；重试必须返回同一业务结果而不是再次执行副作用。';
COMMENT ON COLUMN "IdempotencyRecords"."RequestHash" IS
    '规范化请求内容的摘要；同键同操作但哈希不同必须冲突拒绝，防止错误重放。';
COMMENT ON COLUMN "IdempotencyRecords"."StatusCode" IS
    '首次响应 HTTP 状态码；与响应内容一起用于语义一致的重放。';
COMMENT ON COLUMN "IdempotencyRecords"."ContentType" IS
    '首次响应的媒体类型；保证重放方按原协议解析。';
COMMENT ON COLUMN "IdempotencyRecords"."Status" IS
    '幂等记录处理状态；进行中、完成和失败必须明确区分，结果未知时不得盲目重放非幂等副作用。';
COMMENT ON COLUMN "IdempotencyRecords"."CreatedAt" IS
    '首次接收操作的 UTC 时间；用于幂等保留、审计和超时核对。';

COMMENT ON TABLE "Inbox" IS
    '租户内事件消费去重账本；MessageId 唯一保证至少一次投递只产生一次业务副作用。';
COMMENT ON COLUMN "Inbox"."Id" IS
    '收件记录标识；用于消费追踪，不替代事件 MessageId 的去重语义。';
COMMENT ON COLUMN "Inbox"."TenantId" IS
    '事件所属租户，是强制 RLS 边界；消费者不得跨租户处理。';
COMMENT ON COLUMN "Inbox"."MessageId" IS
    '上游事件标识；与 TenantId 唯一，重复投递必须识别并返回既有处理结果。';
COMMENT ON COLUMN "Inbox"."EventType" IS
    '事件稳定类型；消费者必须按类型和 schema 版本选择兼容处理逻辑。';
COMMENT ON COLUMN "Inbox"."PayloadJson" IS
    '事件负载的脱敏快照；不得内联秘密，处理时仍须重新鉴权和校验租户归属。';
COMMENT ON COLUMN "Inbox"."Status" IS
    '消费状态；处理中、已处理和失败需可重试且不得绕过 MessageId 去重。';
COMMENT ON COLUMN "Inbox"."CreatedAt" IS
    '收件记录的 UTC 时间；用于投递延迟、重试和保留审计。';

COMMENT ON TABLE "InterviewStages" IS
    '候选人面试阶段及评分证据；阶段状态和评分必须可追溯，不能覆盖历史评估。';
COMMENT ON COLUMN "InterviewStages"."Id" IS
    '面试阶段记录标识；用于流程推进和审计。';
COMMENT ON COLUMN "InterviewStages"."TenantId" IS
    '面试所属租户，是强制 RLS 边界；候选人资料不得跨租户访问。';
COMMENT ON COLUMN "InterviewStages"."CandidateId" IS
    '关联候选人标识；只能引用同一租户的候选人。';
COMMENT ON COLUMN "InterviewStages"."Stage" IS
    '面试阶段类型；阶段顺序和准入条件由招聘状态机执行。';
COMMENT ON COLUMN "InterviewStages"."Status" IS
    '阶段处理状态；完成、跳过或失败必须保留理由和审计。';
COMMENT ON COLUMN "InterviewStages"."Score" IS
    '该阶段岗位相关能力评分；必须保留评分口径，不能单独决定录用。';
COMMENT ON COLUMN "InterviewStages"."EvidenceJson" IS
    '评分依据的结构化证据；应最小化个人信息并禁止写入秘密。';
COMMENT ON COLUMN "InterviewStages"."CreatedAt" IS
    '阶段记录创建的 UTC 时间，用于流程顺序和审计。';

COMMENT ON TABLE "Memberships" IS
    '账户与租户的成员授权关系；Role 和 CapabilitiesJson 限定本租户能力。SELECT 的租户策略与用户策略按 permissive OR 组合，用户策略只放行 app.user_id 本人关系且不限制写入，应用仍须按当前租户过滤。';
COMMENT ON COLUMN "Memberships"."Id" IS
    '成员关系标识；用于审计和幂等更新。';
COMMENT ON COLUMN "Memberships"."TenantId" IS
    '成员关系所属租户，是强制 RLS 边界；切换租户必须重新建立事务上下文。';
COMMENT ON COLUMN "Memberships"."UserId" IS
    '关联账户标识；SELECT 时用户策略可单独放行其匹配 app.user_id 的本人关系，与租户策略为 OR；写入仍只受租户策略和应用鉴权约束。';
COMMENT ON COLUMN "Memberships"."Role" IS
    '租户内角色；角色只是授权输入，执行层仍须校验具体 Capability 和资源范围。';
COMMENT ON COLUMN "Memberships"."CapabilitiesJson" IS
    '成员的有效能力快照；不得保存秘密，能力撤销必须通过新状态或删除传播到会话与执行层。';

COMMENT ON TABLE "Notifications" IS
    '租户内持久站内通知；RecipientId、Level 和 Status 控制投递与已读状态，SignalR 仅作实时增强。';
COMMENT ON COLUMN "Notifications"."Id" IS
    '通知标识；用于精确缓存失效、已读和审计。';
COMMENT ON COLUMN "Notifications"."TenantId" IS
    '通知所属租户，是强制 RLS 边界；通知内容和接收人不得跨租户泄露。';
COMMENT ON COLUMN "Notifications"."RecipientId" IS
    '接收账户的用户标识；投递和查询必须同时校验 TenantId 与当前账户状态，当前实现不将其解释为 EmployeeId。';
COMMENT ON COLUMN "Notifications"."Level" IS
    '通知严重级别；用于路由和展示，不能自动扩大接收人权限。';
COMMENT ON COLUMN "Notifications"."Title" IS
    '通知标题；只保存必要摘要，不得包含密钥、完整 Prompt 或原始敏感日志。';
COMMENT ON COLUMN "Notifications"."Status" IS
    '通知可读状态；Postgres 是事实源，SignalR 丢失后必须能重拉。';
COMMENT ON COLUMN "Notifications"."CreatedAt" IS
    '通知创建的 UTC 时间；用于排序、去重和保留。';

COMMENT ON TABLE "OrgDelegations" IS
    '岗位子树内的限时授权委托；ActionsJson、BudgetLimitMinor 和 ExpiresAt 共同限定上限，受托人不得转授权。';
COMMENT ON COLUMN "OrgDelegations"."Id" IS
    '委托标识；用于授权校验、撤销和审计。';
COMMENT ON COLUMN "OrgDelegations"."TenantId" IS
    '委托所属租户，是强制 RLS 边界；委托双方和岗位必须同租户。';
COMMENT ON COLUMN "OrgDelegations"."DelegatorEmployeeId" IS
    '委托发起员工；只能委托其合法拥有的权限，不能创造新权限。';
COMMENT ON COLUMN "OrgDelegations"."DelegateEmployeeId" IS
    '受托员工；每次执行仍须校验委托状态、期限和动作范围。';
COMMENT ON COLUMN "OrgDelegations"."SubtreePositionId" IS
    '委托覆盖的岗位子树根；决定组织范围，不覆盖该子树之外的资源。';
COMMENT ON COLUMN "OrgDelegations"."ActionsJson" IS
    '明确允许的动作集合；采用稳定能力标识，空集合不得解释为全部权限。';
COMMENT ON COLUMN "OrgDelegations"."BudgetLimitMinor" IS
    '委托动作预算上限的最小货币单位整数；必须结合租户币种解释且不得突破原预算。';
COMMENT ON COLUMN "OrgDelegations"."ExpiresAt" IS
    '委托失效的 UTC 时间；过期即拒绝，不能凭历史记录继续执行。';
COMMENT ON COLUMN "OrgDelegations"."Status" IS
    '委托生命周期状态；撤销或过期后必须立即停止执行并保留审计。';

COMMENT ON TABLE "Outbox" IS
    '与业务状态同事务提交的待投递事件；至少一次投递依赖 Inbox 去重，Attempts/ProcessedAt 支持重试和核对。';
COMMENT ON COLUMN "Outbox"."Id" IS
    'Outbox 事件标识；下游以此关联投递和审计，不得复用。';
COMMENT ON COLUMN "Outbox"."TenantId" IS
    '事件所属租户，是强制 RLS 边界；Dispatcher 必须按租户领取和投递。';
COMMENT ON COLUMN "Outbox"."EventType" IS
    '事件稳定类型；消费者按类型和 schema 版本兼容处理。';
COMMENT ON COLUMN "Outbox"."PayloadJson" IS
    '事件负载快照；必须脱敏并包含足够租户/主体引用，不得保存秘密。';
COMMENT ON COLUMN "Outbox"."Status" IS
    '投递状态；Pending 可重试，失败/毒消息必须显式记录并告警，不能静默丢弃。';
COMMENT ON COLUMN "Outbox"."Attempts" IS
    '已尝试投递次数；用于有界退避、死信阈值和运维核对。';
COMMENT ON COLUMN "Outbox"."CreatedAt" IS
    '事件写入的 UTC 时间；与业务状态同事务产生，用于顺序和延迟监控。';
COMMENT ON COLUMN "Outbox"."ProcessedAt" IS
    '投递完成的 UTC 时间；为空表示尚未完成，结果未知时须核对而非盲目重放副作用。';

COMMENT ON TABLE "Positions" IS
    '租户内动态组织岗位；ParentPositionId 与规范 Path 维护无环且最多四级的树，Version 记录组织修订次数。';
COMMENT ON COLUMN "Positions"."Id" IS
    '岗位标识；用于组织关系、授权和 Path 叶节点，不得跨租户复用。';
COMMENT ON COLUMN "Positions"."TenantId" IS
    '岗位所属租户，是强制 RLS 边界；父岗位、员工和任职必须属于同一租户。';
COMMENT ON COLUMN "Positions"."Name" IS
    '岗位名称；权限按能力而非名称判断，重命名不得改变授权语义。';
COMMENT ON COLUMN "Positions"."Responsibilities" IS
    '岗位职责说明；用于组织规划和招聘，不直接授予执行权限。';
COMMENT ON COLUMN "Positions"."CapabilitiesJson" IS
    '岗位能力集合的结构化快照；关键授权仍须经能力策略和执行层校验，不能把 JSON 值直接当作授权决定。';
COMMENT ON COLUMN "Positions"."BudgetMinor" IS
    '岗位预算的最小货币单位整数；币种由租户章程约定，分配不得突破公司预算。';
COMMENT ON COLUMN "Positions"."ParentPositionId" IS
    '父岗位标识；根岗位为空，必须同租户且不能形成环或超过四级。';
COMMENT ON COLUMN "Positions"."Path" IS
    '规范 ltree 路径 root.<岗位GUID>[.<子岗位GUID>]，最多四级岗位即 nlevel≤5；创建、合并、拆分和回填必须同步父路径、租户与深度约束。';
COMMENT ON COLUMN "Positions"."Status" IS
    '岗位生命周期状态；关闭或合并必须处理任职、槽位和派生权限。';
COMMENT ON COLUMN "Positions"."Version" IS
    '岗位修订号；创建、合并、拆分和状态变更时递增，用于审计和变更追踪，不单独构成数据库乐观锁。';

COMMENT ON TABLE "Projects" IS
    '租户项目聚合与预算快照；Stage/Health 描述生命周期，BudgetMinor/SpentMinor/Currency 必须使用同一币种口径。';
COMMENT ON COLUMN "Projects"."Id" IS
    '项目标识；任务、里程碑和成果通过该标识归属。';
COMMENT ON COLUMN "Projects"."TenantId" IS
    '项目所属租户，是强制 RLS 边界；项目下资源不得跨租户引用。';
COMMENT ON COLUMN "Projects"."Name" IS
    '项目名称；用于展示和检索，不作为权限依据。';
COMMENT ON COLUMN "Projects"."Mission" IS
    '项目目标与范围说明；变更需审计，不能借文案扩大预算或权限。';
COMMENT ON COLUMN "Projects"."Owner" IS
    '项目负责人的兼容展示引用；权威人员授权以成员和能力记录为准。';
COMMENT ON COLUMN "Projects"."Stage" IS
    '项目生命周期阶段；阶段门禁决定允许动作和发布条件。';
COMMENT ON COLUMN "Projects"."Health" IS
    '确定性规则计算的项目健康状态；不得由模型自由改写。';
COMMENT ON COLUMN "Projects"."TargetAt" IS
    '项目目标的 UTC 时间；用于提醒和延期审计。';
COMMENT ON COLUMN "Projects"."BudgetMinor" IS
    '项目预算的最小货币单位整数；与 Currency 一起解释，调整必须留审计。';
COMMENT ON COLUMN "Projects"."SpentMinor" IS
    '项目已结算支出的最小货币单位快照；当前由用量投影累加，是项目展示口径，调整必须可审计且不得重复计量。';
COMMENT ON COLUMN "Projects"."Currency" IS
    '项目预算和支出使用的 ISO 4217 币种；不得从语言推断或混用不同币种金额。';
COMMENT ON COLUMN "Projects"."UpdatedAt" IS
    '项目最近更新的 UTC 时间；用于列表排序、缓存失效和审计。';

COMMENT ON TABLE "SchemaVersions" IS
    '控制面数据库迁移历史；Version 唯一且只增不改，应用启动只读校验当前版本而不执行 DDL。';
COMMENT ON COLUMN "SchemaVersions"."Version" IS
    '已应用迁移版本号兼主键；同一版本只能记录一次，运行时要求精确匹配当前基线。';
COMMENT ON COLUMN "SchemaVersions"."Name" IS
    '迁移的稳定名称；用于运维核对，不保存连接串或凭据。';
COMMENT ON COLUMN "SchemaVersions"."AppliedAt" IS
    '迁移提交的 UTC 时间；用于审计和升级追溯。';

COMMENT ON TABLE "TaskCoordinations" IS
    '任务协作请求的权威状态机；租约 Token、Version 和所有权条件防止旧 Worker 覆盖新结果。';
COMMENT ON COLUMN "TaskCoordinations"."Id" IS
    '协作请求标识；用于幂等命令、审计和过程追踪。';
COMMENT ON COLUMN "TaskCoordinations"."TenantId" IS
    '协作所属租户，是强制 RLS 边界；任务、项目和员工必须同租户。';
COMMENT ON COLUMN "TaskCoordinations"."TaskId" IS
    '关联任务标识；协作动作不得脱离任务权限和预算边界。';
COMMENT ON COLUMN "TaskCoordinations"."ProjectId" IS
    '关联项目标识；用于项目范围和过程可视化，必须与 TaskId 所属项目一致。';
COMMENT ON COLUMN "TaskCoordinations"."Kind" IS
    '协作请求类型，如委派、交接、求助或打断；不同类型具有独立状态转换。';
COMMENT ON COLUMN "TaskCoordinations"."Status" IS
    '协作状态机当前值；所有权转移必须显式确认，聊天消息本身不改变状态。';
COMMENT ON COLUMN "TaskCoordinations"."RequesterEmployeeId" IS
    '发起协作的员工；仅能在其能力和任务权限范围内请求。';
COMMENT ON COLUMN "TaskCoordinations"."TargetEmployeeId" IS
    '目标员工；接受前必须重新校验租户、能力、WIP 和独立性约束。';
COMMENT ON COLUMN "TaskCoordinations"."Priority" IS
    '协作优先级；影响排队和提醒，不绕过审批或能力限制。';
COMMENT ON COLUMN "TaskCoordinations"."Summary" IS
    '协作摘要；不得包含秘密、完整 Prompt 或未脱敏源码。';
COMMENT ON COLUMN "TaskCoordinations"."DetailsJson" IS
    '协作细节的结构化扩展；关键状态和所有权仍以本表字段为准。';
COMMENT ON COLUMN "TaskCoordinations"."ResponseJson" IS
    '目标员工的响应快照；为空表示尚未响应，不替代显式确认状态。';
COMMENT ON COLUMN "TaskCoordinations"."LeaseToken" IS
    '当前协作租约的 fencing 标识；提交所有权或状态时必须匹配，旧 Token 不得覆盖新租约。';
COMMENT ON COLUMN "TaskCoordinations"."SortKey" IS
    '稳定队列排序键；与状态和租户联合确定处理顺序，避免依赖写入时间造成抖动。';
COMMENT ON COLUMN "TaskCoordinations"."CreatedAt" IS
    '协作请求创建的 UTC 时间；用于 SLA 和审计。';
COMMENT ON COLUMN "TaskCoordinations"."UpdatedAt" IS
    '最近状态更新的 UTC 时间；与 Version 一同支持并发核对。';
COMMENT ON COLUMN "TaskCoordinations"."Deadline" IS
    '协作应完成的 UTC 时间；超时必须按状态机升级或拒绝，不能静默完成。';
COMMENT ON COLUMN "TaskCoordinations"."CompletedAt" IS
    '协作闭环的 UTC 时间；为空表示未完成，终态写入后不得回退。';
COMMENT ON COLUMN "TaskCoordinations"."Version" IS
    '乐观并发版本；所有所有权和状态变更必须按当前版本条件更新。';

COMMENT ON TABLE "Tenants" IS
    '租户根记录；Id 是所有租户业务的隔离锚点，Status 控制可用性，删除和清除必须走显式生命周期流程。';
COMMENT ON COLUMN "Tenants"."Id" IS
    '租户唯一标识；RLS 的 app.tenant_id 必须按 UUID 值匹配，GUID 文本形式差异不产生新租户。';
COMMENT ON COLUMN "Tenants"."Name" IS
    '租户名称；用于管理和展示，不作为授权依据。';
COMMENT ON COLUMN "Tenants"."CompanyName" IS
    '租户对应公司的展示名称；第一梯队保持一租户一公司。';
COMMENT ON COLUMN "Tenants"."Status" IS
    '租户生命周期状态；停用或删除后必须阻止新业务并传播会话、索引和对象访问撤销。';

COMMENT ON TABLE "Users" IS
    '全局账户身份投影；Id 关联登录和成员关系，Email 属于个人数据且不得进入普通日志。';
COMMENT ON COLUMN "Users"."Id" IS
    '账户唯一标识；跨租户成员关系通过 UserId 引用，不复制账户秘密。';
COMMENT ON COLUMN "Users"."Email" IS
    '账户登录邮箱；属于个人数据，应用按规范化值执行唯一性校验并脱敏；当前数据库无邮箱唯一索引，不能将其当作数据库约束。';
COMMENT ON COLUMN "Users"."DisplayName" IS
    '账户展示名称；属于个人数据，只保留界面所需信息。';

COMMENT ON TABLE "WorkEntries" IS
    '员工工作台账事实；OccurredAt 保留业务发生时间，CostMinor/Currency 记录最小货币单位成本且不得重复入账。';
COMMENT ON COLUMN "WorkEntries"."Id" IS
    '工作台账条目标识；用于审计、证据引用和幂等重放。';
COMMENT ON COLUMN "WorkEntries"."TenantId" IS
    '台账所属租户，是强制 RLS 边界；员工和任务必须同租户。';
COMMENT ON COLUMN "WorkEntries"."EmployeeId" IS
    '完成工作的员工标识；用于绩效和成本归属，不改变任务所有权。';
COMMENT ON COLUMN "WorkEntries"."TaskId" IS
    '关联任务标识；允许为空表示非任务工作，但不得绕过项目或预算归属规则。';
COMMENT ON COLUMN "WorkEntries"."OccurredAt" IS
    '工作实际发生的 UTC 时间；迟到条目按发生周期归属，不能按写入时间重复计量。';
COMMENT ON COLUMN "WorkEntries"."Title" IS
    '工作内容摘要；不得包含秘密、完整 Prompt 或未脱敏个人数据。';
COMMENT ON COLUMN "WorkEntries"."EventType" IS
    '稳定工作事件类型；用于分类、检索和绩效规则。';
COMMENT ON COLUMN "WorkEntries"."Status" IS
    '台账处理状态；完成、撤销和更正必须可审计。';
COMMENT ON COLUMN "WorkEntries"."NextAction" IS
    '后续行动摘要；仅作协作提示，不自动授予执行权限。';
COMMENT ON COLUMN "WorkEntries"."CostMinor" IS
    '工作成本的最小货币单位整数；必须与 Currency 一起解释，作为成本归属证据，重复条目不得重复计量。';
COMMENT ON COLUMN "WorkEntries"."Currency" IS
    '工作成本使用的 ISO 4217 币种；不得从员工或租户语言推断。';
COMMENT ON COLUMN "WorkEntries"."EvidenceJson" IS
    '工作证据的结构化引用；应保存来源和摘要，不内联秘密或完整敏感日志。';

COMMENT ON TABLE "Milestones" IS
    '项目里程碑；本表无 TenantId 或独立 RLS，应用必须经 Projects 校验租户和权限，AcceptanceRule 定义可验证的完成门槛。';
COMMENT ON COLUMN "Milestones"."Id" IS
    '里程碑标识；用于项目计划、验收和审计。';
COMMENT ON COLUMN "Milestones"."ProjectId" IS
    '所属项目标识；数据库级联删除仅处理关系，应用仍须按项目校验租户和权限。';
COMMENT ON COLUMN "Milestones"."Name" IS
    '里程碑名称；用于项目计划和状态展示。';
COMMENT ON COLUMN "Milestones"."TargetAt" IS
    '里程碑目标的 UTC 时间；用于提醒和延期审计。';
COMMENT ON COLUMN "Milestones"."Status" IS
    '里程碑生命周期状态；完成必须满足 AcceptanceRule 并保留证据。';
COMMENT ON COLUMN "Milestones"."AcceptanceRule" IS
    '确定性的里程碑验收规则；不得由模型自由解释后直接标记完成。';

COMMENT ON TABLE "Tasks" IS
    '项目内可认领任务及执行租约；LeaseOwner/LeaseToken/LeaseExpiresAt 构成 fencing，Version 防止并发覆盖。';
COMMENT ON COLUMN "Tasks"."Id" IS
    '任务标识；用于需求拆解、执行尝试、台账和成果关联。';
COMMENT ON COLUMN "Tasks"."TenantId" IS
    '任务所属租户，是强制 RLS 边界；任务、项目和员工必须同租户。';
COMMENT ON COLUMN "Tasks"."ProjectId" IS
    '所属项目标识；数据库级联删除仅处理关系，应用仍须保持租户和权限一致。';
COMMENT ON COLUMN "Tasks"."Title" IS
    '任务标题；只保存必要摘要，不得包含秘密或完整敏感源码。';
COMMENT ON COLUMN "Tasks"."TaskType" IS
    '稳定任务类型；决定所需能力、预算和评审规则。';
COMMENT ON COLUMN "Tasks"."Status" IS
    '任务状态机当前值；认领、执行、评审和完成必须沿合法转换推进。';
COMMENT ON COLUMN "Tasks"."Assignee" IS
    '认领人的兼容展示字段；权威执行者以 AssigneeEmployeeId 和当前租约为准。';
COMMENT ON COLUMN "Tasks"."Weight" IS
    '需求拆分权重；同一需求下权重必须精确拆分，用于 WIP 和绩效计算。';
COMMENT ON COLUMN "Tasks"."CostMinor" IS
    '任务预算或成本的最小货币单位整数；必须与 Currency 一致且不得突破项目预算。';
COMMENT ON COLUMN "Tasks"."Currency" IS
    '任务金额使用的 ISO 4217 币种；不得从语言或地区推断。';
COMMENT ON COLUMN "Tasks"."LeaseOwner" IS
    '当前租约持有者引用；与 Token 和过期时间共同防止多 Worker 同时执行。';
COMMENT ON COLUMN "Tasks"."LeaseToken" IS
    '任务执行租约的 fencing 标识；旧 Worker 提交结果时必须匹配当前 Token，否则拒绝覆盖。';
COMMENT ON COLUMN "Tasks"."LeaseExpiresAt" IS
    '租约失效的 UTC 时间；为空或过期表示可重新认领，释放时需核对 Token。';
COMMENT ON COLUMN "Tasks"."Attempt" IS
    '执行尝试序号；每次重新认领递增，用于审计和幂等副作用关联。';
COMMENT ON COLUMN "Tasks"."CheckpointJson" IS
    '安全点检查点的脱敏结构化引用；大内容存 RustFS，不能把秘密或完整 Prompt 作为检查点。';
COMMENT ON COLUMN "Tasks"."AssigneeEmployeeId" IS
    '实际认领员工标识；执行前必须校验能力、状态、WIP 和租户。';
COMMENT ON COLUMN "Tasks"."AccountableEmployeeId" IS
    '任务结果问责员工标识；用于审计和评审，不替代执行者授权。';
COMMENT ON COLUMN "Tasks"."UpdatedAt" IS
    '任务最近更新的 UTC 时间；与 Version 一同支持并发核对。';
COMMENT ON COLUMN "Tasks"."Version" IS
    '任务乐观并发版本；认领、状态和结果提交必须按当前版本条件更新。';

COMMENT ON TABLE "MemoryEmbeddings" IS
    '租户级 pgvector 记忆投影；仅保存 claim 的有限 256 维向量和索引元数据，claim 正文与权限仍在 JSON 本地模块。';
COMMENT ON COLUMN "MemoryEmbeddings"."ClaimId" IS
    '被索引的 claim 标识；与 TenantId 复合主键允许不同租户复用相同标识，不表示正文已迁入 PostgreSQL。';
COMMENT ON COLUMN "MemoryEmbeddings"."TenantId" IS
    '向量投影所属租户，是强制 RLS 读写边界；召回还必须先按应用 ACL 预过滤候选。';
COMMENT ON COLUMN "MemoryEmbeddings"."ContentHash" IS
    'claim 内容校验摘要；用于识别陈旧向量，内容变化后必须重新索引。';
COMMENT ON COLUMN "MemoryEmbeddings"."ModelVersion" IS
    '生成向量的嵌入模型/版本；模型或维度语义变化时必须重建投影。';
COMMENT ON COLUMN "MemoryEmbeddings"."Vector" IS
    '固定的 256 维有限浮点向量；使用余弦距离排序，NaN/Infinity 和错误维度必须在写入前拒绝。';
COMMENT ON COLUMN "MemoryEmbeddings"."IndexedAt" IS
    '该投影最近写入的 UTC 时间；用于陈旧检测和索引删除传播。';

COMMENT ON INDEX "IX_Approvals_TenantId_State_ExpiresAt" IS
    '按租户、审批状态和失效时间定位待处理审批，支持超时作业和有界分页。';
COMMENT ON INDEX "IX_Artifacts_TenantId_ProjectId_UploadState" IS
    '按租户和项目筛选对象元数据及上传状态，用于成果列表和未完成上传核对。';
COMMENT ON INDEX "IX_AuditCheckpoints_TenantId_ThroughSeq" IS
    '保证每租户审计序号只有一个检查点，并支持按序号验证和锚定。';
COMMENT ON INDEX "IX_AuditRecords_TenantId_Seq" IS
    '保证租户内审计顺序唯一，支持哈希链校验、增量导出和按序翻页。';
COMMENT ON INDEX "IX_CandidateAppeals_TenantId_CandidateId_Status" IS
    '按租户和候选人查找待处理或历史申诉，控制招聘复核队列。';
COMMENT ON INDEX "IX_Candidates_TenantId_PositionId_Status" IS
    '按租户、岗位和招聘状态筛选候选人，支持有界流程查询。';
COMMENT ON INDEX "IX_Employees_TenantId_EmployeeNo" IS
    '保证租户内永久员工工号唯一，防止离职再入职或并发创建重用编号。';
COMMENT ON INDEX "IX_EmploymentPeriods_TenantId_EmployeeId_EndedAt" IS
    '按员工追溯任职历史并定位当前任期，支持离职和 primary 槽位校验。';
COMMENT ON INDEX "IX_EmploymentPeriods_TenantId_PositionId_EndedAt" IS
    '按岗位追溯任职历史并定位在岗人员，支持主任职者唯一性和编制检查。';
COMMENT ON INDEX "IX_FairnessAudits_TenantId_PositionId_Period" IS
    '按租户、岗位和周期复查公平审计，避免重复统计和遗漏。';
COMMENT ON INDEX "IX_IdempotencyRecords_TenantId_Key_Operation" IS
    '保证同租户同操作的幂等键唯一，是防止重复副作用的核心约束。';
COMMENT ON INDEX "IX_Inbox_TenantId_MessageId" IS
    '保证同租户事件只被记录一次，支撑至少一次投递下的消费去重。';
COMMENT ON INDEX "IX_InterviewStages_TenantId_CandidateId_Stage" IS
    '按租户、候选人和阶段检索面试进度与评分证据。';
COMMENT ON INDEX "IX_Milestones_ProjectId" IS
    '加速项目里程碑读取和项目删除级联，租户范围仍由关联项目控制。';
COMMENT ON INDEX "IX_Notifications_TenantId_RecipientId_CreatedAt" IS
    '按接收人和创建时间分页拉取站内通知，支持 SignalR 断线后的快照补齐。';
COMMENT ON INDEX "IX_OrgDelegations_TenantId_DelegateEmployeeId_Status" IS
    '按受托员工查找有效委托，执行层可快速缩小需重新鉴权的候选集。';
COMMENT ON INDEX "IX_Outbox_Status_CreatedAt" IS
    '按投递状态和创建时间领取待处理事件，支持有界重试和死信核对；租户过滤仍必须显式执行。';
COMMENT ON INDEX "IX_Positions_TenantId_Name" IS
    '按租户检索岗位名称，用于组织列表和选择器。';
COMMENT ON INDEX "IX_Projects_TenantId_UpdatedAt" IS
    '按租户最近变更时间分页同步项目，支持缓存失效和仪表盘刷新。';
COMMENT ON INDEX "IX_TaskCoordinations_TenantId_Kind_Status_SortKey" IS
    '按协作类型、状态和稳定排序键领取队列，避免高频事件导致界面抖动。';
COMMENT ON INDEX "IX_TaskCoordinations_TenantId_TargetEmployeeId_Status_SortKey" IS
    '按目标员工分页待处理协作，支持个人工作台和所有权确认。';
COMMENT ON INDEX "IX_TaskCoordinations_TenantId_TaskId_Kind_Status" IS
    '按任务追踪各类协作当前状态，支撑过程审计和防重复处理。';
COMMENT ON INDEX "IX_Tasks_ProjectId" IS
    '加速项目任务读取和项目删除级联，租户范围仍由任务 TenantId/RLS 控制。';
COMMENT ON INDEX "IX_Tasks_TenantId_AssigneeEmployeeId_Status" IS
    '按员工和状态查找可执行或进行中任务，支持认领队列与离职中断。';
COMMENT ON INDEX "IX_Tasks_TenantId_ProjectId_Status" IS
    '按租户和项目筛选任务状态，支持项目交付看板和有界列表。';
COMMENT ON INDEX "IX_WorkEntries_TenantId_EmployeeId_OccurredAt" IS
    '按员工和业务发生时间检索工作台账，支持绩效、成本归属和迟到更正。';
COMMENT ON INDEX "IX_MemoryEmbeddings_Vector" IS
    'HNSW 余弦索引加速权限预过滤后的向量 top-k；索引存在不保证每次查询被选中，也不替代 RLS/ACL。';
COMMENT ON INDEX "IX_Positions_Path" IS
    'GiST ltree 索引支持租户内岗位子树与深度查询；父路径、无环和层级不变量仍由应用维护。';

COMMENT ON INDEX "PK_Approvals" IS
    '审批主键索引，保证记录唯一并加速按审批标识的证据和执行核对。';
COMMENT ON INDEX "PK_Artifacts" IS
    '对象元数据主键索引，保证版本记录唯一并加速成果引用查找。';
COMMENT ON INDEX "PK_AuditCheckpoints" IS
    '检查点主键索引，保证记录唯一并加速检查点读取。';
COMMENT ON INDEX "PK_AuditRecords" IS
    '审计行主键索引，仅用于物理记录定位；租户链顺序由 TenantId/Seq 唯一索引与应用哈希校验共同维护。';
COMMENT ON INDEX "PK_CandidateAppeals" IS
    '申诉主键索引，保证记录唯一并加速复核引用。';
COMMENT ON INDEX "PK_Candidates" IS
    '候选人主键索引，保证记录唯一并加速招聘流程关联。';
COMMENT ON INDEX "PK_Charters" IS
    '以 TenantId 为主键的索引，从数据库层保证每租户最多一份章程并加速租户读取。';
COMMENT ON INDEX "PK_Employees" IS
    '员工主键索引，保证记录唯一并加速任务、任职和审计关联。';
COMMENT ON INDEX "PK_EmploymentPeriods" IS
    '任职周期主键索引，保证记录唯一并加速历史追溯。';
COMMENT ON INDEX "PK_FairnessAudits" IS
    '公平审计主键索引，保证记录唯一并加速复核引用。';
COMMENT ON INDEX "PK_IdempotencyRecords" IS
    '幂等记录主键索引，仅用于内部定位；客户端去重另由 TenantId/Key/Operation 唯一索引保证。';
COMMENT ON INDEX "PK_Inbox" IS
    '收件记录主键索引，仅用于内部定位；事件去重另由 TenantId/MessageId 唯一索引保证。';
COMMENT ON INDEX "PK_InterviewStages" IS
    '面试阶段主键索引，保证记录唯一并加速候选人流程关联。';
COMMENT ON INDEX "PK_Memberships" IS
    '成员关系主键索引，保证记录唯一；租户/用户授权仍需 RLS 和执行层校验。';
COMMENT ON INDEX "PK_Notifications" IS
    '通知主键索引，保证记录唯一并加速已读与缓存失效定位。';
COMMENT ON INDEX "PK_OrgDelegations" IS
    '委托主键索引，保证记录唯一并加速授权核对。';
COMMENT ON INDEX "PK_Outbox" IS
    'Outbox 主键索引，保证事件唯一并加速投递跟踪。';
COMMENT ON INDEX "PK_Positions" IS
    '岗位主键索引，保证记录唯一并加速组织关系查找。';
COMMENT ON INDEX "PK_Projects" IS
    '项目主键索引，保证记录唯一并加速任务和成果关联。';
COMMENT ON INDEX "PK_SchemaVersions" IS
    '版本主键索引，保证每个迁移版本只记录一次。';
COMMENT ON INDEX "PK_TaskCoordinations" IS
    '协作主键索引，保证记录唯一并加速所有权和状态核对。';
COMMENT ON INDEX "PK_Tenants" IS
    '租户主键索引，保证根记录唯一并加速租户解析。';
COMMENT ON INDEX "PK_Users" IS
    '账户主键索引，保证记录唯一并加速成员关系查找。';
COMMENT ON INDEX "PK_WorkEntries" IS
    '工作台账主键索引，保证记录唯一并加速证据和成本追溯。';
COMMENT ON INDEX "PK_Milestones" IS
    '里程碑主键索引，保证记录唯一并加速项目计划关联。';
COMMENT ON INDEX "PK_Tasks" IS
    '任务主键索引，保证记录唯一并加速认领、租约和结果核对。';
COMMENT ON INDEX "PK_MemoryEmbeddings" IS
    'TenantId/ClaimId 复合主键索引，隔离租户并保证同一 claim 投影唯一。';

COMMENT ON CONSTRAINT "PK_Approvals" ON "Approvals" IS
    '审批记录唯一标识；执行时仍须校验审批状态、有效期和当前权限。';
COMMENT ON CONSTRAINT "PK_Artifacts" ON "Artifacts" IS
    '对象元数据唯一标识；Ready 内容不可覆盖，新内容必须生成新版本。';
COMMENT ON CONSTRAINT "PK_AuditCheckpoints" ON "AuditCheckpoints" IS
    '检查点记录唯一标识；审计前缀唯一性另由 TenantId/ThroughSeq 唯一索引保证。';
COMMENT ON CONSTRAINT "PK_AuditRecords" ON "AuditRecords" IS
    '审计行物理唯一标识；业务链顺序由 TenantId/Seq 唯一索引与应用哈希校验共同维护。';
COMMENT ON CONSTRAINT "PK_CandidateAppeals" ON "CandidateAppeals" IS
    '申诉记录唯一标识；处理必须保留原理由和审计。';
COMMENT ON CONSTRAINT "PK_Candidates" ON "Candidates" IS
    '候选人记录唯一标识；招聘数据访问仍受租户和盲评权限约束。';
COMMENT ON CONSTRAINT "PK_Charters" ON "Charters" IS
    '以 TenantId 为主键，从数据库层保证每租户最多一份当前章程。';
COMMENT ON CONSTRAINT "PK_Employees" ON "Employees" IS
    '员工记录唯一标识；租户内永久工号唯一性另由唯一索引保证。';
COMMENT ON CONSTRAINT "PK_EmploymentPeriods" ON "EmploymentPeriods" IS
    '任职周期唯一标识；当前 primary 任职和槽位唯一性由应用事务维护。';
COMMENT ON CONSTRAINT "PK_FairnessAudits" ON "FairnessAudits" IS
    '公平审计唯一标识；异常结论必须可复核且不得覆盖原始证据。';
COMMENT ON CONSTRAINT "PK_IdempotencyRecords" ON "IdempotencyRecords" IS
    '幂等记录唯一标识；客户端键唯一性由 TenantId/Key/Operation 唯一索引保证。';
COMMENT ON CONSTRAINT "PK_Inbox" ON "Inbox" IS
    '收件记录唯一标识；事件去重由 TenantId/MessageId 唯一索引保证。';
COMMENT ON CONSTRAINT "PK_InterviewStages" ON "InterviewStages" IS
    '面试阶段唯一标识；阶段推进必须保留评分证据和审计。';
COMMENT ON CONSTRAINT "PK_Memberships" ON "Memberships" IS
    '成员关系唯一标识；租户和用户双重校验及 RLS 策略不能被主键替代。';
COMMENT ON CONSTRAINT "PK_Notifications" ON "Notifications" IS
    '通知唯一标识；已读和缓存失效以该标识定位。';
COMMENT ON CONSTRAINT "PK_OrgDelegations" ON "OrgDelegations" IS
    '委托唯一标识；有效期、状态和动作范围仍决定是否可执行。';
COMMENT ON CONSTRAINT "PK_Outbox" ON "Outbox" IS
    '待投递事件唯一标识；投递重试不得生成新的业务事件。';
COMMENT ON CONSTRAINT "PK_Positions" ON "Positions" IS
    '岗位唯一标识；父子同租户、无环和 Path 一致性由应用及迁移校验维护。';
COMMENT ON CONSTRAINT "PK_Projects" ON "Projects" IS
    '项目唯一标识；预算、任务和成果仍按租户边界访问。';
COMMENT ON CONSTRAINT "PK_SchemaVersions" ON "SchemaVersions" IS
    '版本号唯一，确保每个迁移只记录一次并支持幂等重放。';
COMMENT ON CONSTRAINT "PK_TaskCoordinations" ON "TaskCoordinations" IS
    '协作请求唯一标识；状态和所有权转换必须匹配 LeaseToken 与 Version。';
COMMENT ON CONSTRAINT "PK_Tenants" ON "Tenants" IS
    '租户根记录唯一标识；所有租户业务通过 TenantId 引用该边界。';
COMMENT ON CONSTRAINT "PK_Users" ON "Users" IS
    '账户唯一标识；账户秘密和会话凭据不存储在该投影表。';
COMMENT ON CONSTRAINT "PK_WorkEntries" ON "WorkEntries" IS
    '工作台账唯一标识；成本不得通过重复条目重复入账。';
COMMENT ON CONSTRAINT "PK_Milestones" ON "Milestones" IS
    '里程碑唯一标识；完成仍须满足验收规则并保留证据。';
COMMENT ON CONSTRAINT "FK_Milestones_Projects_ProjectId" ON "Milestones" IS
    '里程碑必须属于已存在的项目，项目删除时级联移除里程碑；数据库外键本身不校验 TenantId。';
COMMENT ON CONSTRAINT "PK_Tasks" ON "Tasks" IS
    '任务唯一标识；认领、租约 fencing 和版本条件不能被主键替代。';
COMMENT ON CONSTRAINT "FK_Tasks_Projects_ProjectId" ON "Tasks" IS
    '任务必须属于已存在的项目，项目删除时级联移除任务；数据库外键本身不校验 TenantId。';
COMMENT ON CONSTRAINT "PK_MemoryEmbeddings" ON "MemoryEmbeddings" IS
    '以 TenantId/ClaimId 为复合主键，既隔离租户又允许不同租户复用 claim 标识；claim 正文仍在 JSON。';

INSERT INTO "SchemaVersions" ("Version", "Name", "AppliedAt")
VALUES (6, 'controlplane-chinese-catalog-comments', now())
ON CONFLICT ("Version") DO NOTHING;
COMMIT;
