/*
		Copyright (c) 2014, MOEX. All rights reserved.
		
		Plaza-2 Client Gate API Usage Sample.
		Replication DataStream Client.
		
		All the software and documentation included in this and any
		other MOEX CGate Releasese is copyrighted by MOEX.
		
		Redistribution and use in source and binary forms, with or without
		modification, are permitted only by the terms of a valid
		software license agreement with MOEX.
		
		THIS SOFTWARE IS PROVIDED "AS IS" AND MICEX-RTS DISCLAIMS ALL WARRANTIES
		EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION, ANY IMPLIED WARRANTIES OF
		NON-INFRINGEMENT, MERCHANTABILITY OR FITNESS FOR A PARTICULAR
		PURPOSE.  MICEX-RTS DOES NOT WARRANT THAT USE OF THE SOFTWARE WILL BE
		UNINTERRUPTED OR ERROR-FREE.  MICEX-RTS SHALL NOT, UNDER ANY CIRCUMSTANCES, BE
		LIABLE TO LICENSEE FOR LOST PROFITS, CONSEQUENTIAL, INCIDENTAL, SPECIAL OR
		INDIRECT DAMAGES ARISING OUT OF OR RELATED TO THIS AGREEMENT OR THE
		TRANSACTIONS CONTEMPLATED HEREUNDER, EVEN IF MICEX-RTS HAS BEEN APPRISED OF
		THE LIKELIHOOD OF SUCH DAMAGES.
*/

/*
	Настоящий пример показывает сборку биржевого стакана для определенного инструмента,
	чей isin_id указан в аргументах командной строки.
	Также указываются максимальное количество выводимых заявок (глубина стакана, 
	0 - для вывода без ограничений), название выходного файла и признак реверсивной сортировки (r).

	This sample represents working with order book for specific instrument 
	whose isin_id is set in command line arguments.
	The other arguments are: order book depth (max quantity of elements in output, 0 for unlimited output),
	output file name and reverse sort flag (r).
*/

using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Linq;

using ru.micexrts.cgate;
using System.Runtime.InteropServices;
using ru.micexrts.cgate.message;

namespace orderbook
{
	enum Direction
	{
		Buy, Sell
	};
	enum Action
	{
		Cancel,
		Add,
		Exec
	}

	// номер ревизии таблицы. соответствует данным CG_MSG_P2REPL_CLEARDELETED
	// table revision number. corresponding to CG_MSG_P2REPL_CLEARDELETED data
	class RevisionInfo
	{
		public UInt32 TableIndex;
		public Int64 TableRevision;

		public RevisionInfo(UInt32 index, Int64 revision)
		{
			this.TableIndex = index;
			this.TableRevision = revision;
		}
	};

	class CGateDisconnectException : CGateException
	{
		public CGateDisconnectException(string message)
			: base(message) { }
		public CGateDisconnectException(ErrorCode errorCode, string message)
			: base(errorCode, message) { }
		public CGateDisconnectException(int errorCode, string message)
			: base(errorCode, message) { }
	};

	// данные заявки (активной или агрегированной), 
	// сохраненные в стакане
	//
	// active or aggregate order data
	// stored in order books
	class Order
	{
		Int64 id;
		double price;
		Int32 amount;
		RevisionInfo revision; // ignored for aggregate orders

		public Order(Int64 id, double price, Int32 amount, RevisionInfo rev)
		{
			this.id = id;
			this.price = price;
			this.amount = amount;
			this.revision = rev;
		}

		// чтение данных заявки из stream_data message
		//
		// reading order data from stream_data message
		public Order(StreamDataMessage message)
		{
			this.id = message[OrderBookApp.SchemeInfo.FieldNames.OrderId].asLong();
			this.price = message[OrderBookApp.SchemeInfo.FieldNames.Price].asDouble();
			this.amount = message[OrderBookApp.SchemeInfo.FieldNames.Amount].asInt();
			Int64 tableRevision = message[OrderBookApp.SchemeInfo.FieldNames.ReplRevision].asLong();
			this.revision = new RevisionInfo((uint)message.MsgIndex, tableRevision);
		}

		public Int64 Id
		{
			get
			{
				return this.id;
			}
		}

		public double Price
		{
			get
			{
				return this.price;
			}
		}

		public Int32 Amount
		{
			get
			{
				return this.amount;
			}
			set
			{
				this.amount = value;
			}
		}

		public RevisionInfo Revision
		{
			get
			{
				return this.revision;
			}
		}

		public override string ToString()
		{
			return String.Format("Id = {0}, price = {1}, amount = {2}, table = {3}, rev = {4}",
				this.id, this.price, this.amount, this.revision.TableIndex, this.revision.TableRevision);
		}
	};

	// расширенные данные заявки
	// используются для хранения частей составной бизнес-транзакции
	//
	// extended order data
	// used for storing parts of composite business transaction
	class OrderDataEx : Order
	{
		Direction direction;
		Int32 isinId;
		Int64 replAct;
		Action action;

		public OrderDataEx(Int64 id, double price, Int32 amount, RevisionInfo rev,
			Direction direction, Int32 isinId, Int64 replAct, Action action)
			: base(id, price, amount, rev)
		{
			this.direction = direction;
			this.isinId = isinId;
			this.replAct = replAct;
			this.action = action;
		}

		// чтение данных заявки из stream_data message
		//
		// reading order data from stream_data message
		public OrderDataEx(StreamDataMessage message) :
			base(message)
		{
			Byte intDir = message[OrderBookApp.SchemeInfo.FieldNames.Direction].asByte();
			this.direction = (intDir == 1) ? Direction.Buy : Direction.Sell;
			this.isinId = message[OrderBookApp.SchemeInfo.FieldNames.IsinId].asInt();
			this.replAct = message[OrderBookApp.SchemeInfo.FieldNames.ReplAct].asLong();
			Byte intAction = message[OrderBookApp.SchemeInfo.FieldNames.Action].asByte();
			this.action = (Action)intAction;
		}

		public Direction Direction
		{
			get
			{
				return this.direction;
			}
		}

		public Int32 IsinId
		{
			get
			{
				return this.isinId;
			}
		}

		public Int64 ReplAct
		{
			get
			{
				return this.replAct;
			}
		}

		public Action Action
		{
			get
			{
				return this.action;
			}
		}

		public override string ToString()
		{
			return base.ToString() +
				String.Format(", Dir = {0}, IsinId = {1}, ReplAct = {2}, Action = {3}",
				this.direction, this.isinId, this.replAct, this.action);
		}
	}

	// вспомогательная функция для сравнения цены покупки. лучшая цена - самая большая
	//
	// helper function object for buying price comparison. bigger price is better
	class OrderComparatorBuy : IComparer<double>
	{
		public int Compare(double x, double y)
		{
			return -Comparer<double>.Default.Compare(x, y);
		}
	};

	// вспомогательная функция для сравнения цены продажи. лучшая цена - самая маленькая
	//
	// helper function object for selling price comparison. less price is better
	class OrderComparatorSell : IComparer<double>
	{
		public int Compare(double x, double y)
		{
			return Comparer<double>.Default.Compare(x, y);
		}
	};

	// сортированный в указанном направлении контейнер для активных и агрегированных заявок
	//
	// sorted in specified direction container for active and aggregate
	class Orders
	{
		static IComparer<double>[] comparers;

		// активные заявки
		// по соображениям производительности сортировка 
		// контейнера происходит только при печати содержимого
		// 
		// active orders
		// by perfomance reasons container sorting occurs only when printing
		Dictionary<Int64, Order> activeOrders;
		// содержит агрегированные заявки (суммарный объем заявок с одинаковой ценой)
		// по соображениям производительности сортировка 
		// контейнера происходит только при печати содержимого
		// 
		// aggregate orders
		// by perfomance reasons container sorting occurs only when printing
		Dictionary<double, Order> aggregateOrders;
		Direction direction;

		static Orders()
		{
			comparers = new IComparer<double>[2];
			comparers[(int)Direction.Buy] = new OrderComparatorBuy();
			comparers[(int)Direction.Sell] = new OrderComparatorSell();
		}

		public Orders(Direction dir)
		{
			this.activeOrders = new Dictionary<long, Order>();
			this.aggregateOrders = new Dictionary<double, Order>();
			this.direction = dir;
		}

		private Order FindOrderByPrice(double price)
		{
			Order order = null;
			try
			{
				order = this.aggregateOrders[price];
			}
			catch (KeyNotFoundException) { }
			return order;
		}

		// вычесть объем заявки из агрегированного объема по данной цене
		// удалить заявку, если агрегированный объем стал равен 0
		//
		// subtract order amount from the aggregate amount. 
		// delete record when modified aggregate amount = 0
		private void ModifyAggregateAmount(double price, int subtrahendAmount)
		{
			Order order = this.FindOrderByPrice(price);
			if (order == null)
			{
				CGate.LogInfo(String.Format(
					"Got remove operator for non-existing order price = {0}", price));
			}

			if (order.Amount == subtrahendAmount)
			{
				CGate.LogDebug("Deleting aggregate record");
				this.aggregateOrders.Remove(price);
			}
			else if (order.Amount > subtrahendAmount)
			{
				CGate.LogDebug(String.Format("Aggregate amount for price = {0}: {1} -> {2}",
					order.Price, order.Amount, order.Amount - subtrahendAmount));
				order.Amount -= subtrahendAmount;
			}
			else
			{
				throw new CGateException(String.Format(
					"Subtrahend amount {0} is bigger than aggregate amount {1}",
					subtrahendAmount,
					order.Amount));
			}
		}

		// добавление активной заявки.
		// создание новой агрегированной завки, если нет заявки с данной ценой
		//
		// place new active order. 
		// alter aggregate record corresponding to it (if exist) or create new
		public void Add(RevisionInfo rev, Int64 id, double price, Int32 amount)
		{
			Order activeOrder = new Order(id, price, amount, rev);
			this.activeOrders[id] = activeOrder;

			Order aggregateOrder = this.FindOrderByPrice(price);
			if (aggregateOrder == null)
			{
				CGate.LogDebug("New record");
				aggregateOrder = new Order(id, price, amount, rev);
				this.aggregateOrders.Add(price, aggregateOrder);
			}
			else
			{
				CGate.LogDebug(String.Format("Found existing record. Amount {0} -> {1}",
					aggregateOrder.Amount, aggregateOrder.Amount + amount));
				aggregateOrder.Amount += amount;
			}
		}

		// удалить активную заявку. вычесть ее объем из агрегированной
		//
		// delete active record. subtract it from aggregate amount
		public void Remove(Int64 id)
		{
			try
			{
				Order orderToRemove = this.activeOrders[id];
				this.activeOrders.Remove(id);
				CGate.LogDebug("Deleting active order record");
				this.ModifyAggregateAmount(orderToRemove.Price, orderToRemove.Amount);
			}
			catch (KeyNotFoundException)
			{
				CGate.LogInfo(String.Format(
					"Got remove operator for non-existing order id = {0}", id));
			}
		}

		// изменить объем активной заявки. поправить агрегированную запись
		//
		// change amount of active record. alter aggregate amount
		public void Modify(Int64 id, Int32 newAmount)
		{
			if (newAmount == 0)
			{
				this.Remove(id);
			}
			else
			{
				try
				{
					Order orderToModify = this.activeOrders[id];
					Int32 deltaAmount = orderToModify.Amount - newAmount;
					orderToModify.Amount = newAmount;
					this.ModifyAggregateAmount(orderToModify.Price, deltaAmount);
				}
				catch (KeyNotFoundException)
				{
					CGate.LogInfo(String.Format(
						"Got modify operator for non-existing order id = {0}, amount = {1}", id, newAmount));
				}
			}
		}

		// удалить все заявки (активные и агрегированные)
		//
		// delete all orders (active and aggregate)
		public void Clear()
		{
			this.activeOrders.Clear();
			this.aggregateOrders.Clear();
		}

		// обработчик CG_MSG_P2REPL_CLEARDELETED
		// удалить все заявки с ревизией, старшей, чем указанная
		//
		// CG_MSG_P2REPL_CLEARDELETED handler
		// delete all orders with revision older than specified
		public void OnClearDeleted(RevisionInfo rev)
		{
			List<Int64> keysToRemove = new List<Int64>();
			foreach (KeyValuePair<Int64, Order> p in this.activeOrders)
			{
				if ((p.Value.Revision.TableIndex == rev.TableIndex) &&
					(p.Value.Revision.TableRevision < rev.TableRevision))
				{
					keysToRemove.Add(p.Key);
				}
			}
			foreach (Int64 key in keysToRemove)
			{
				this.Remove(key);
			}
		}

		// записать содержимое агрегированных заявок в текстовый файл
		//
		// write aggregate contents to text file
		public void Print(TextWriter writer, uint depth, bool reverse)
		{
			if (depth == 0)
			{
				depth = UInt32.MaxValue;
			}

			// по соображениям производительности сортировка 
			// контейнера происходит только при печати содержимого
			// by perfomance reasons container sorting occurs only when printing
			double[] keys = this.aggregateOrders.Keys.ToArray();

			// сортировка в прямом направлении
			// direct sorting
			if (!reverse)
			{
				Array.Sort(keys, comparers[(int)this.direction]);
			}
			else
			{
				// сортировка в обратном направлении
				// reverse sorting
				Array.Sort(keys, comparers[1 - (int)this.direction]);
			}

			for (uint i = 0; i < Math.Min(depth, keys.Count()); ++i)
			{
				Order order;
				if (this.aggregateOrders.TryGetValue(keys[i], out order))
				{
					writer.WriteLine("\t\tprice = {0:F2}\tamount = {1}",
					order.Price, order.Amount);
				}
			}
		}
	};

	// стакан для определенного инструмента
	// содержит по одному контейнеру 'Orders' для заявок на покупку и продажу
	//
	// order book for single instrument
	// contains one 'Orders' container for buy orders and one for sell orders
	class OrderBook
	{
		Int32 isinId;
		Orders[] orders;

		public OrderBook(Int32 isinId)
		{
			this.isinId = isinId;
			this.orders = new Orders[2];
			this.orders[(int)Direction.Buy] = new Orders(Direction.Buy);
			this.orders[(int)Direction.Sell] = new Orders(Direction.Sell);
		}

		public void Add(Direction dir, RevisionInfo rev, Int64 id, double price, Int32 amount)
		{
			this.orders[(int)dir].Add(rev, id, price, amount);
		}

		public void Modify(Direction dir, Int64 id, Int32 newAmount)
		{
			this.orders[(int)dir].Modify(id, newAmount);
		}

		public void Remove(Direction dir, Int64 id)
		{
			this.orders[(int)dir].Remove(id);
		}

		public void Clear()
		{
			this.orders[(int)Direction.Buy].Clear();
			this.orders[(int)Direction.Sell].Clear();
		}

		public void OnClearDeleted(RevisionInfo rev)
		{
			this.orders[(int)Direction.Buy].OnClearDeleted(rev);
			this.orders[(int)Direction.Sell].OnClearDeleted(rev);
		}

		public void Print(Direction direction, TextWriter writer, uint depth, bool reverse)
		{
			this.orders[(int)direction].Print(writer, depth, reverse);
		}
	};

	// основной объект - приложение OrderBookApp
	// реализует логику обработки сообщений
	//
	// main order book application
	// implements message callback and other message handling functions
	public class OrderBookApp
	{
		static bool bExit = false;

		Connection connection;
		Listener listener;

		int sessionId;
		OrderBook orderBook;
		uint targetIsinId;
		uint depth;
		string outfile;
		bool reverse;
		Int64 lastRevision;

		List<OrderDataEx> currentOrders;

		// описание серверной схемы
		//
		// server scheme description
		public struct SchemeInfo
		{
			// индексы таблиц в схеме
			// 
			// indices of tables in scheme
			public struct TableIndices
			{
				public long Orders;
				public long OrdersLog;
				public long SysEvents;
			};
			public TableIndices Indices;

			// названия таблиц в схеме
			//
			// names of tables in scheme
			public struct TableNames
			{
				public const string Orders = "orders";
				public const string OrdersLog = "orders_log";
				public const string SysEvents = "sys_events";
			};

			// названия полей в таблицах
			//
			// names of fields in tables
			public struct FieldNames
			{
				public const string ReplAct = "replAct";
				public const string Action = "action";
				public const string OrderId = "id_ord";
				public const string IsinId = "isin_id";
				public const string Price = "price";
				public const string Amount = "amount_rest";
				public const string Status = "status";
				public const string Direction = "dir";
				public const string SessionId = "sess_id";
				public const string ReplRevision = "replRev";
			};

			public bool Ready;

			public const Int32 StatusMessageNonSystem = 0x4;
			public const Int32 StatusEndBusinessTransaction = 0x1000;

		};
		SchemeInfo schemeInfo;

		SchemeDesc Scheme
		{
			get
			{
				return this.listener.Scheme;
			}
		}

		public static bool Exit
		{
			get
			{
				return bExit;
			}
		}

		public OrderBookApp(Connection conn, String settings, uint targetIsinId,
			uint depth, string outfile, bool reverse)
		{
			this.targetIsinId = targetIsinId;
			this.orderBook = new OrderBook((int)targetIsinId);
			this.sessionId = 0;
			this.currentOrders = new List<OrderDataEx>();
			this.schemeInfo.Ready = false;
			this.schemeInfo.Indices.Orders =
				this.schemeInfo.Indices.OrdersLog =
				this.schemeInfo.Indices.SysEvents = -1;

			this.connection = conn;
			this.listener = new Listener(conn, settings);
			this.listener.Handler += new Listener.MessageHandler(OrdbookMessageHandler);
			this.listener.Open();
			if ((this.listener.State != State.Active) && ((this.listener.State != State.Opening)))
			{
				throw new CGateException("Unable to connect");
			}
			this.targetIsinId = targetIsinId;
			this.depth = depth;
			this.outfile = outfile;
			this.reverse = reverse;
			this.lastRevision = 0;
		}

		~OrderBookApp()
		{
			try
			{
				this.listener.Close();
			}
			catch (CGateException ex)
			{
				CGate.LogError(ex.Message);
			}
			catch (Exception) { }

			this.Cleanup();
		}

		public static void ConsoleCancelEventHandler(object sender, ConsoleCancelEventArgs e)
		{
			bExit = true;
		}

		// основной обработчик сообщений
		//
		// main message callback function
		int OrdbookMessageHandler(Connection connection, Listener listener, Message message)
		{
			switch (message.Type)
			{
				// открытие нового соединения с сервером
				// инициализировать схему
				//
				// opening new server connection
				// initialize scheme description
				case MessageType.MsgOpen:
					{
						CGate.LogDebug("Open message received");
						this.Cleanup();

						for (int i = 0; i < this.listener.Scheme.Messages.Length; ++i)
						{
							MessageDesc md = this.listener.Scheme.Messages[i];
							if (md.Name == SchemeInfo.TableNames.Orders)
							{
								this.schemeInfo.Indices.Orders = i;
							}
							else if (md.Name == SchemeInfo.TableNames.OrdersLog)
							{
								this.schemeInfo.Indices.OrdersLog = i;
							}
							else if (md.Name == SchemeInfo.TableNames.SysEvents)
							{
								this.schemeInfo.Indices.SysEvents = i;
							}
						}

						if ((this.schemeInfo.Indices.Orders >= 0) &&
							(this.schemeInfo.Indices.OrdersLog >= 0) &&
							(this.schemeInfo.Indices.SysEvents >= 0))
						{
							this.schemeInfo.Ready = true;
						}
						break;
					}

				// обрабатывать только таблицы 'orders', 'orders_log' и 'sys_events'
				// получать снимок базы данных по FORTS_FUTORDERBOOK_REPL.orders
				// и обновления по FORTS_ORDLOG_REPL.orders_log
				// использовать 'sys_events' для определения текущего sess_id
				// пропускать остальные таблицы
				// пропускать сообщения для инструментов, отличных от _target_isin_id
				//
				// process only 'orders', 'orders_log' and 'sys_events' tables
				// receive database snapshot from FORTS_FUTORDERBOOK_REPL.orders 
				// and updates from FORTS_ORDLOG_REPL.orders_log
				// use 'sys_events' for determining current sess_id
				// skip other tables
				// skip messages for instruments other than _target_isin_id
				case MessageType.MsgStreamData:
					{
						if (!this.schemeInfo.Ready)
						{
							break;
						}
						StreamDataMessage sdMessage = (StreamDataMessage)message;
						// обработка 'sys_events'
						// 'sys_events' handling
						if (sdMessage.MsgIndex == this.schemeInfo.Indices.SysEvents)
						{
							// определения номера сессии
							// determining session id
							int sessionId = sdMessage[SchemeInfo.FieldNames.SessionId].asInt();
							this.UpdateSessionId(sessionId);
							break;
						}

						if ((sdMessage.MsgIndex == this.schemeInfo.Indices.Orders) ||
							(sdMessage.MsgIndex == this.schemeInfo.Indices.OrdersLog))
						{
							UInt32 isinId = sdMessage[SchemeInfo.FieldNames.IsinId].asUInt();
							// пропуск сообщений для других инструментов
							// skip messages for another instruments
							if (isinId != this.targetIsinId)
							{
								break;
							}

							Int32 status = sdMessage[SchemeInfo.FieldNames.Status].asInt();
							// пропуск внесистемных заявок
							// ignore non-system orders
							if ((status & SchemeInfo.StatusMessageNonSystem) > 0)
							{
								break;
							}

							Int32 sessionId = sdMessage[SchemeInfo.FieldNames.SessionId].asInt();
							// пропуск сообщений с предыдущих сессий
							// ignore messages from older sessions
							if (sessionId < this.sessionId)
							{
								break;
							}
							// сравнение полученного sess_id с текущим
							// изменение текущего номера сессии при обновлении
							// compare session id with current
							// update if session_id from message data is newer than current
							this.UpdateSessionId(sessionId);

							// заполнение заявки из потокового сообщения
							// filling order data from stream message
							OrderDataEx currentOrder = new OrderDataEx(sdMessage);
							// запоминание текущего номера ревизии (для 'orders_log')
							// remembering current revision (for 'orders_log')
							if (sdMessage.MsgIndex == this.schemeInfo.Indices.OrdersLog)
							{
								this.lastRevision = currentOrder.Revision.TableRevision;
							}

							// данная заявка является не последней в составной бизнес-транзакции
							// сохраняем ее без обработки
							// current order is not last in composite business-transaction
							// save it without processing
							if ((status & SchemeInfo.StatusEndBusinessTransaction) == 0)
							{
								this.currentOrders.Add(currentOrder);
							}
							else
							{
								// обработка полной бизнес-транзакции
								// processing complete business transaction

								// если бизнес-транзакция состоит из одной заявки
								// in case of single-ordered business-transaction
								if (this.currentOrders.Count == 0)
								{
									this.ProcessOrder(currentOrder);
								}
								else
								{
									// обработка всех заявок транзакции по очереди
									// processing all transaction orders one after another

									this.currentOrders.Add(currentOrder);
									foreach (OrderDataEx order in this.currentOrders)
									{
										this.ProcessOrder(order);
									}
									// очистка текущей очереди заявок
									// clear current order queue
									this.currentOrders.Clear();
								}
							}
						}
						break;
					}

				// очистить все данные при смене номера жизни
				//
				// clear all data when life number is changed
				case MessageType.MsgP2ReplLifeNum:
					{
						CGate.LogDebug("Life number changed");
						this.Cleanup();
						break;
					}

				// удалить все заявки с ревизией, старшей, чем указанная
				//
				// clear older orders
				case MessageType.MsgP2ReplClearDeleted:
					{
						P2ReplClearDeletedMessage cdMessage = (P2ReplClearDeletedMessage)message;
						CGate.LogDebug(String.Format("ClearDeleted received. Table = {0}, revision = {1}",
							cdMessage.TableIdx, cdMessage.TableRev));

						if ((cdMessage.TableIdx == this.schemeInfo.Indices.Orders) ||
							(cdMessage.TableIdx == this.schemeInfo.Indices.OrdersLog))
						{
							RevisionInfo revision = new RevisionInfo(cdMessage.TableIdx, cdMessage.TableRev);
							this.orderBook.OnClearDeleted(revision);

							if (cdMessage.TableRev == P2ReplClearDeletedMessage.MaxRevision)
							{
								this.lastRevision = 0;
							}
						}
						break;
					}

				// закрытие соединения 
				// записать содержимое стакана в файл
				// произвести очистку всех данных
				//
				// closing current connection
				// write order book contents into file
				// perform complete cleanup
				case MessageType.MsgClose:
					{
						this.Print(this.outfile);
						this.Cleanup();
						break;
					}

				// данное сообщение говорит о получении полной реплики базы данных
				// с этого момента мы будем получать обновления
				//
				// this message tells that we have completed database replica
				// since this moment we shall receive updates only
				case MessageType.MsgP2ReplOnline:
					{
						break;
					}
			}

			return (int)ErrorCode.Ok;
		}

		// обработка активной заявки
		//
		// processing active order
		void ProcessOrder(OrderDataEx order)
		{
			// если repl_act != 0, заявка удаляется
			// при этом в поле repl_act записан предыдущий номер ревизии заявки
			// in case of repl_act != 0 current order is deleted
			// at that repl_act contains previous revision number of this order
			if (order.ReplAct != 0)
			{
				this.orderBook.Remove(order.Direction, order.Id);
				return;
			}

			switch (order.Action)
			{
				case Action.Add:
					{
						this.orderBook.Add(order.Direction, order.Revision, order.Id, order.Price, order.Amount);
						break;
					}

				case Action.Exec:
					{
						this.orderBook.Modify(order.Direction, order.Id, order.Amount);
						break;
					}

				case Action.Cancel:
					{
						this.orderBook.Remove(order.Direction, order.Id);
						break;
					}

				default:
					{
						throw new CGateException(String.Format(
							"Unknown action {0}", order.Action));
					}
			}
		}

		void Cleanup()
		{
			this.orderBook.Clear();
		}

		bool UpdateSessionId(Int32 id)
		{
			// если номер сессии сменился на следующий
			// if sess_id is changed to next
			if (id > this.sessionId)
			{
				// исключаем случай, когда sess_id еще не был инициализирован
				// удаляем все данные с предыдущей сессии
				// avoiding an occurence when sess_id not being initialized yet
				// deleting all data from previous session
				if (this.sessionId > 0)
				{
					this.Print(String.Format(
						"orderbook.NET session {0}.log", this.sessionId));
					this.Cleanup();
				}
				this.sessionId = id;
				return true;
			}
			else
			{
				return false;
			}
		}

		// основной цикл приложения
		//
		// main application loop body
		ErrorCode Run()
		{
			if (this.connection.State != State.Active)
			{
				CGate.LogError(String.Format(
					"Error: connection state is {0}", this.connection.State));
			}
			ErrorCode result = ErrorCode.Ok;
			switch (this.connection.State)
			{
				case State.Error:
					{
						this.connection.Close();
						result = ErrorCode.IncorrectState;
						break;
					}

				case State.Closed:
					{
						try
						{
							this.connection.Open();
						}
						catch (CGateException ex)
						{
							CGate.LogError(ex.Message);
							result = ErrorCode.Internal;
						}
						break;
					}

				case State.Active:
					{
						result = this.connection.Process(0);
						if ((result != ErrorCode.Ok) && (result != ErrorCode.TimeOut))
						{
							CGate.LogInfo(String.Format(
								"Warning: connection state request failed: {0}", result));
						}

						switch (this.listener.State)
						{
							case State.Closed:
								this.listener.Open();
								break;

							case State.Error:
								this.listener.Close();
								throw new CGateDisconnectException("Disconnect");
						}
						break;
					}

			}
			return result;
		}

		// записать содержимое стакана в текстовый файл
		//
		// write current order books content to text file
		void Print(string fileName)
		{
			TextWriter writer = new StreamWriter(fileName);
			writer.WriteLine("BUY:");
			this.orderBook.Print(Direction.Buy, writer, this.depth, this.reverse);
			writer.WriteLine("SELL:");
			this.orderBook.Print(Direction.Sell, writer, this.depth, this.reverse);
			writer.Close();
		}

		// записать содержимое стакана в текстовый файл
		// добавить в название файла номер текущей ревизии
		//
		// write current order books content to text file
		// add current revision number to file name
		void PrintLastRevision()
		{
			this.Print("ordbook " + this.targetIsinId + "." + this.lastRevision + ".log");
		}

		public static void Main(string[] args)
		{
			Console.CancelKeyPress += ConsoleCancelEventHandler;

			uint targetIsinId = 0;
			uint depth = 0;
			string outfile = "";
			try
			{
				targetIsinId = Convert.ToUInt32(args[0]);
				depth = Convert.ToUInt32(args[1]);
				outfile = args[2];
			}
			catch (Exception)
			{
				Console.WriteLine("Usage:\n\tordbook ISIN_ID depth outfile r(optional)\n");
				Console.WriteLine("where\n\tISIN_ID is unique instrument ID (e.g. 167566)");
				Console.WriteLine("\tdepth is max orderbook size (0 for unlimited size)");
				Console.WriteLine("\toutfile is output file path");
				Console.WriteLine("\tr for reverse sorting");
				return;
			}
			bool reverse = false;
			try
			{
				reverse = (args[3] == "r");
			}
			catch (Exception) { }

			CGate.Open("ini=netordbook.ini;key=11111111;minloglevel=trace");
			Connection conn = new Connection("p2tcp://127.0.0.1:4001;app_name=orderbook_net");
			try
			{
				conn.Open();
			}
			catch (CGateException ex)
			{
				CGate.LogError(ex.Message);
				return;
			}

			String settings = "p2ordbook://FORTS_ORDLOG_REPL;snapshot=FORTS_FUTORDERBOOK_REPL";
			OrderBookApp theApp = new OrderBookApp(
				conn, settings, targetIsinId, depth, outfile, reverse);

			while (!Exit)
			{
				try
				{
					theApp.Run();
				}
				catch (CGateDisconnectException)
				{
					CGate.LogInfo("Disconnection");
				}
				if (Console.KeyAvailable)
				{
					// print OrderBook on "Enter"
					if (Console.ReadKey().KeyChar == '\r')
					{
						theApp.PrintLastRevision();
					}
				}
			}
		}
	};
}
