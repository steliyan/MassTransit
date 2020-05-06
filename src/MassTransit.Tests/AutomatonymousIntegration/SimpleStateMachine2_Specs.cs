// Copyright 2007-2014 Chris Patterson, Dru Sellers, Travis Smith, et. al.
//
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use
// this file except in compliance with the License. You may obtain a copy of the
// License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software distributed
// under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR
// CONDITIONS OF ANY KIND, either express or implied. See the License for the
// specific language governing permissions and limitations under the License.
namespace MassTransit.Tests.AutomatonymousIntegration
{
    using System;
    using System.Threading.Tasks;
    using Automatonymous;
    using MassTransit.Courier;
    using MassTransit.Courier.Contracts;
    using MassTransit.Saga;
    using MassTransit.Testing;
    using NUnit.Framework;
    using TestFramework;


    [TestFixture]
    public class Using_a_simple_state_machine2 :
        InMemoryTestFixture
    {
        protected override void ConfigureInMemoryReceiveEndpoint(IInMemoryReceiveEndpointConfigurator configurator)
        {
            configurator.StateMachineSaga(_machine, _repository);

            configurator.Handler<DoWork>(async context =>
            {
                var builder = new RoutingSlipBuilder(NewId.NextGuid());

                await builder.AddSubscription(
                    context.SourceAddress,
                    RoutingSlipEvents.Completed | RoutingSlipEvents.Supplemental,
                    RoutingSlipEventContents.None,
                    x => x.Send<Stop>(new { context.Message.CorrelationId }));

                await context.Execute(builder.Build());
            });
        }

        readonly TestStateMachine _machine;
        readonly InMemorySagaRepository<Instance> _repository;

        public Using_a_simple_state_machine2()
        {
            _machine = new TestStateMachine();
            _repository = new InMemorySagaRepository<Instance>();
        }


        class Instance :
            SagaStateMachineInstance
        {
            public Instance(Guid correlationId, Uri responseAddress, Guid requestId)
            {
                CorrelationId = correlationId;
                ResponseAddress = responseAddress;
                RequestId = requestId;
            }

            protected Instance()
            {
            }

            public State CurrentState { get; set; }
            public Guid CorrelationId { get; set; }
            public Uri ResponseAddress { get; set; }
            public Guid RequestId { get; set; }
        }


        class TestStateMachine :
            MassTransitStateMachine<Instance>
        {
            public TestStateMachine()
            {
                InstanceState(x => x.CurrentState);

                Event(() => Started,
                    x => x.SetSagaFactory(m => new Instance(m.Message.CorrelationId, m.ResponseAddress, (Guid)m.RequestId)));
                Event(() => Stopped);

                Initially(
                    When(Started)
                        .TransitionTo(Running)
                        .PublishAsync(x => x.Init<DoWork>(new { x.Data.CorrelationId })));

                During(Running,
                    When(Stopped)
                        .SendAsync(
                            x => x.Instance.ResponseAddress,
                            x => x.Init<StartResponse>(new
                            {
                                // This doesn't work:
                                __RequestId = x.Instance.RequestId,
                                x.Data.CorrelationId
                            })
                            // This works:
                            // ,x =>
                            // {
                            //     var consumeEventCtx = x.GetOrAddPayload<ConsumeEventContext<Instance>>(() => null);
                            //     x.RequestId = consumeEventCtx.Instance.RequestId;
                            // }
                        )
                        .Finalize());
            }

            public State Running { get; private set; }
            public Event<Start> Started { get; private set; }
            public Event<Stop> Stopped { get; private set; }
        }


        class Start :
            CorrelatedBy<Guid>
        {
            public Guid CorrelationId { get; set; }
        }


        class DoWork :
            CorrelatedBy<Guid>
        {
            public Guid CorrelationId { get; set; }
        }


        class StartResponse :
            CorrelatedBy<Guid>
        {
            public Guid CorrelationId { get; set; }
        }


        class Stop :
            CorrelatedBy<Guid>
        {
            public Guid CorrelationId { get; set; }
        }


        [Test]
        public async Task Should_return_response_when_using_request_id_with_double_dash()
        {
            Guid sagaId = Guid.NewGuid();

            var client = Bus.CreateRequestClient<Start>();

            await client.GetResponse<StartResponse>(new { CorrelationId = sagaId });
        }
    }
}
