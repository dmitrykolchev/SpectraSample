
#ifndef _scheme_H_
#define _scheme_H_



// Scheme "scheme" description


#pragma pack(push, 4)

    static const char scheme_scheme_string[] = "|STRING|XeNrdlktvgkAUhff9FYQNq0pNrHGBmiZ2wdakTXeTkbnqJPOg86D67ztQiqICNiVt0yWck3Pv3PvxiOY7zrwMlKZSTP3h4M73QCSSULGZ-tasbyf-fHYT6WQLHGaRwSsGnsAcpgEBzIJZtKbASHlLQcriReCZfequ6CQIz_UlZO2Gh8Q0GzRojSipDKNTA9VUtBsIKjpvLLFLpW5RMZdWtHTo8qUiaGX3nR4NrKWPVNEEPmUyHA_uTx1ccji0Yi7LSFSnsWc1hNR7bYBXXQxzBxUEdqUjXrwU80LxAj0FnhX01ebFlIWgdF4gwIWEhdYQtnx8vjYt5-UoLiwQrJPILTOUwQb9VyR7Y6qT3m7oFDaAum36Dafo7yBcQ6Q3luup_UK9BazMCrD5BaCV-yIgQznUFnN6-qrD3uZ5SOx3lo4QBJkjSf_4MIuyx0__198fHxG52uzhLgRvKkMyHl3a2GEOva3sKPL7OwvL_4x3DmPm0Q==";


    struct deal
    {
        signed long long replID; // i8
        signed long long replRev; // i8
        signed long long replAct; // i8
        signed int sess_id; // i4
        signed int isin_id; // i4
        signed long long id_deal; // i8
        signed long long xpos; // i8
        signed long long xamount; // i8
        signed long long id_ord_buy; // i8
        signed long long id_ord_sell; // i8
        char price[11]; // d16.5
        struct cg_time_t moment; // t
        unsigned long long moment_ns; // u8
        signed char nosystem; // i1
        
    };


    const size_t sizeof_deal = 108;
    const size_t deal_index = 0;


    struct multileg_deal
    {
        signed long long replID; // i8
        signed long long replRev; // i8
        signed long long replAct; // i8
        signed int sess_id; // i4
        signed int isin_id; // i4
        signed long long id_deal; // i8
        signed long long id_ord_buy; // i8
        signed long long id_ord_sell; // i8
        signed long long xamount; // i8
        char price[11]; // d16.5
        char rate_price[11]; // d16.5
        char swap_price[11]; // d16.5
        struct cg_time_t moment; // t
        unsigned long long moment_ns; // u8
        signed char nosystem; // i1
        
    };


    const size_t sizeof_multileg_deal = 120;
    const size_t multileg_deal_index = 1;


    struct heartbeat
    {
        signed long long replID; // i8
        signed long long replRev; // i8
        signed long long replAct; // i8
        struct cg_time_t server_time; // t
        
    };


    const size_t sizeof_heartbeat = 36;
    const size_t heartbeat_index = 2;


    struct sys_events
    {
        signed long long replID; // i8
        signed long long replRev; // i8
        signed long long replAct; // i8
        signed long long event_id; // i8
        signed int sess_id; // i4
        signed int event_type; // i4
        char message[65]; // c64
        
    };


    const size_t sizeof_sys_events = 108;
    const size_t sys_events_index = 3;


#pragma pack(pop)




#endif //_scheme_H_

