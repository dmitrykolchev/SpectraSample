// ConsoleApplication1.cpp : This file contains the 'main' function. Program execution begins and ends there.
//

#include <windows.h>
#include <stdio.h>
#include <math.h>
#define CG_SHORT_NAMES
#include <cgate.h>
#include "deals.h"

#pragma comment(lib, "cgate64.lib")

int revision = 0;
bool done = false;
CG_RESULT MessageCallback(cg_conn_t* conn, cg_listener_t* listener, struct cg_msg_t* msg, void* data);
BOOL WINAPI InterruptHandler(DWORD reason);
void CheckResult(uint32_t result, bool warning = false);

const char* connectionString = "p2lrpcq://127.0.0.1:4001;app_name=get_deals";
const char* listenerSettings = "p2repl://FORTS_DEALS_REPL;scheme=";
const char* environmentSettings = "ini=repl.ini;key=11111111";
const char* listenerMode = "mode=snapshot+online";

int main()
{
	SetConsoleCtrlHandler(InterruptHandler, 1);

	cg_conn_t* connection = nullptr;
	cg_listener_t* listener = nullptr;
	try
	{
		// open environment
		CheckResult(cg_env_open(environmentSettings));

		// create connection
		CheckResult(cg_conn_new(connectionString, &connection));

		char listenerSettingsWithScheme[4096];

		// build listener settings string
		strcpy_s(listenerSettingsWithScheme, sizeof(listenerSettingsWithScheme), listenerSettings);
		strcat_s(listenerSettingsWithScheme, sizeof(listenerSettingsWithScheme), scheme_scheme_string);
		strcat_s(listenerSettingsWithScheme, sizeof(listenerSettingsWithScheme), ";");

		// create listener with settings specified in listenerSettingsWithScheme
		CheckResult(cg_lsn_new(connection, listenerSettingsWithScheme, MessageCallback, &revision, &listener));

		while (!done)
		{
			uint32_t state;
			CheckResult(cg_conn_getstate(connection, &state));
			if (state == CG_STATE_ERROR)
			{
				CheckResult(cg_conn_close(connection), true);

			}
			else if (state == CG_STATE_CLOSED)
			{
				CheckResult(cg_conn_open(connection, nullptr), true);
			}
			else if (state == CG_STATE_ACTIVE)
			{
				uint32_t result = cg_conn_process(connection, 1, 0);
				if (result != CG_ERR_OK && result != CG_ERR_TIMEOUT)
				{
					cg_log_info("Warning: connection state request failed: %X", result);
				}

				CheckResult(cg_lsn_getstate(listener, &state), true);
				if (state == CG_STATE_CLOSED)
				{
					CheckResult(cg_lsn_open(listener, listenerMode), true);
				}
				else if (state == CG_STATE_ERROR)
				{
					CheckResult(cg_lsn_close(listener), true);
				}
			}
		}
	}
	catch (uint32_t r)
	{
		printf("error core: %d\n", r);
	}

	if (listener)
	{
		cg_lsn_close(listener);
		cg_lsn_destroy(listener);
	}

	// destroy connection
	if (connection)
	{
		cg_conn_close(connection);
		cg_conn_destroy(connection);
	}

	cg_env_close();

	return 0;
}

void ProcessDealData(const cg_msg_streamdata_t* message)
{
	if (message->msg_index == deal_index) {
		deal* pdeal = (deal*)message->data;
		int64_t price_int;
		int8_t price_scale;
		cg_bcd_get(pdeal->price, &price_int, &price_scale);
		double price = ((double)price_int) / (pow(10.0, price_scale));
		printf("| %lld | %d | %10lld | %12.4f | %02d:%02d:%02d.%03d |\n", pdeal->id_deal, pdeal->isin_id, pdeal->xamount, price,
			pdeal->moment.hour,
			pdeal->moment.minute,
			pdeal->moment.second,
			pdeal->moment.msec);
	}
	else if (message->msg_index == heartbeat_index)
	{
		heartbeat* phearbeat = (heartbeat*)message->data;
		//printf("server time: %02d:%02d:%02d.%03d\n", 
		//	phearbeat->server_time.hour,
		//	phearbeat->server_time.minute,
		//	phearbeat->server_time.second,
		//	phearbeat->server_time.msec);
	}
}

CG_RESULT MessageCallback(cg_conn_t* conn, cg_listener_t* listener, struct cg_msg_t* msg, void* data)
{
	switch (msg->type)
	{
		case CG_MSG_STREAM_DATA:
			// data recevied. print data properties (message name, index etc)
			ProcessDealData((struct cg_msg_streamdata_t*)msg);
			break;
		default:
			// Other messages get logged but not handled
			cg_log_info("Message 0x%X", msg->type);
	}

	// code returns 0, since there were no errors.
	// any other error code may be return and it will be logged
	return CG_ERR_OK;
}

BOOL WINAPI InterruptHandler(DWORD reason)
{
	printf("Ctrl-C pressed\n");
	done = true;
	return TRUE;
}

void CheckResult(uint32_t result, bool warning)
{
	if (result != CG_ERR_OK)
	{
		if (warning)
		{
			printf("Warning: %x\n", result);
		}
		else
		{
			printf("Error: %d\n", result);
			throw result;
		}
	}
}
