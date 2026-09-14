Select
    ds.code,
    tt.name, /u
    t.code,
    s.name "State",
    t.state_reason, 
    t.trigger_every,
    t.last_start,
    t.last_end
From task t
Join task_state s on t.state = s.task_state_id
Join task_type tt on t.type_id = tt.task_type_id
Join data_source ds on ds.id = t.data_source_id
